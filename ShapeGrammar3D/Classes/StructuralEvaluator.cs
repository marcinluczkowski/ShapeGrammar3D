using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Reusable static helpers that mirror the private GA evaluation /
    /// FEM / cross-section optimization pipeline of
    /// <see cref="ShapeGrammar3D.Components.GrammarInterpreter_FromSgShape"/>.
    ///
    /// Supports CroSecOpt modes:
    ///   0 = off, 1 = Rect FSD, 2 = SHS catalog FSD.
    /// Other CroSecOpt modes silently fall back to Rect FSD with a warning
    /// returned in <see cref="EvaluationOutcome.Warnings"/>.
    ///
    /// This class is intentionally a copy of the relevant logic so that
    /// GrammarInterpreter_FromSgShape stays untouched. Behavior should
    /// remain consistent with that component for the supported modes.
    /// </summary>
    public static class StructuralEvaluator
    {
        public class EvaluationOutcome
        {
            public List<GAIndividual> EvaluatedPopulation { get; set; } = new List<GAIndividual>();
            public List<SG_Shape> Shapes { get; set; } = new List<SG_Shape>();
            public List<TB_Model> Models { get; set; } = new List<TB_Model>();
            public List<string> Warnings { get; set; } = new List<string>();
        }

        // ─── Public entry points ────────────────────────────────────────

        /// <summary>
        /// Builds the chromosome length list for a list of rules, mirroring
        /// the logic in GrammarInterpreter_FromSgShape.GetChromosomeLengths.
        /// </summary>
        public static List<int> GetChromosomeLengths(List<SG_Rule> rules, SG_Shape inputShape)
        {
            var lengths = new List<int>();
            var initRule = rules.OfType<SG_AutoRule_InitShape_3D>().FirstOrDefault();

            if (initRule == null)
            {
                for (int i = 0; i < rules.Count; i++)
                    lengths.Add(rules[i].GetChromosomeLength(inputShape));
                return lengths;
            }

            int estimatedNodeCount = Math.Max(2, initRule.MaxSupports);
            int fallbackLen = Math.Max(11, estimatedNodeCount + 2);
            var emptyShape = new SG_Shape();

            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] is SG_AutoRule_InitShape_3D)
                {
                    lengths.Add(rules[i].GetChromosomeLength(emptyShape));
                }
                else
                {
                    int ruleSpecific = rules[i].GetChromosomeLength(emptyShape);
                    lengths.Add(ruleSpecific < 11 ? Math.Max(2, ruleSpecific) : fallbackLen);
                }
            }

            return lengths;
        }

        /// <summary>
        /// Reorders rules so SG_AutoRule_InitShape_3D rules come first,
        /// mirroring GrammarInterpreter_FromSgShape.EnsureInitShapeFirst.
        /// </summary>
        public static List<SG_Rule> EnsureInitShapeFirst(List<SG_Rule> rules)
        {
            var initRules = rules.Where(r => r is SG_AutoRule_InitShape_3D).ToList();
            if (initRules.Count == 0) return rules;
            var otherRules = rules.Where(r => !(r is SG_AutoRule_InitShape_3D)).ToList();
            var sorted = new List<SG_Rule>(initRules);
            sorted.AddRange(otherRules);
            return sorted;
        }

        /// <summary>
        /// Evaluates a population of GAIndividuals: applies the rules,
        /// repairs supports and loads, applies self-weight if requested,
        /// computes feasibility, runs FEM, optionally optimizes cross
        /// sections, and writes results back to each individual.
        ///
        /// Supports multi-objective (NumObjectives >= 2): writes objectives
        /// [log(1+dispRatio), utilDev|maxUtil, rawFeas].
        /// For NumObjectives == 1 falls back to single-objective Disp*feasibility
        /// fitness (matches GI_FromSg).
        /// </summary>
        public static EvaluationOutcome EvaluatePopulation(
            List<GAIndividual> population,
            SG_Shape iniShape,
            List<SG_Rule> rules,
            GrammarInterpreterSettings settings,
            FeasibilitySettings feas,
            bool deepCopyOutputs = true)
        {
            var outcome = new EvaluationOutcome();
            if (population == null || population.Count == 0)
                throw new InvalidOperationException("Population not initialized");

            var topoMetrics = (settings.TopologyMetrics ?? new List<int>()).ToList();
            var shapeMetrics = (settings.ShapeMetrics ?? new List<int>()).ToList();
            int numObjectives = Math.Clamp(settings.NumObjectives, 1, 3);
            int croSecOpt = settings.CroSecOpt;
            int csOptIters = Math.Max(1, settings.CSOptIterations);
            double shrinkRatio = settings.ShapeShrinkWrapDetailRatio;
            int utilObjType = settings.UtilObjType;
            int singleObjType = settings.SingleObjType;

            bool useSelfWeight = settings.SelfWeight;
            Vector3d gravityDir = settings.GravityDir;
            if (gravityDir.Length < 1e-9) gravityDir = new Vector3d(0, 0, -1);

            for (int i = 0; i < population.Count; i++)
            {
                var individual = population[i];
                try
                {
                    SG_Genotype gt = CreateGenotypeFromIndividual(individual);
                    SG_Shape shape = iniShape.DeepCopy();

                    for (int j = 0; j < rules.Count; j++)
                    {
                        string msg = rules[j].RuleOperation(ref shape, ref gt);
                        if (i == 0 && msg != null && (msg.Contains("wrong marker")
                                                      || msg.Contains("0 struts")))
                            outcome.Warnings.Add(msg);
                    }

                    shape.RegisterElemsToNodes();
                    EnforceBoundaryConstraints(shape, rules);
                    RepairSupportsAndLoads(shape, rules);

                    if (useSelfWeight)
                        ApplySelfWeightLoads(shape, gravityDir);

                    var feasResult = FeasibilityMetrics.Compute(shape, feas);

                    var tbModel = new TB_Model(shape);
                    var slv = new SolveLS(ref tbModel);
                    TB_Model finalModel = slv.Mdl;

                    if (croSecOpt == 1)
                        finalModel = OptimizeCrossSections_Rect(finalModel, csOptIters);
                    else if (croSecOpt == 2)
                        finalModel = OptimizeCrossSections_SHS(finalModel, csOptIters);
                    else if (croSecOpt > 0)
                    {
                        outcome.Warnings.Add(string.Format(
                            "CroSecOpt mode {0} not supported by StructuralEvaluator; falling back to Rect FSD.",
                            croSecOpt));
                        finalModel = OptimizeCrossSections_Rect(finalModel, csOptIters);
                    }

                    double rawDisp = CalculateMaxNodalDisplacement(finalModel);
                    double spanL = ComputeSpanL(finalModel);
                    double slsLimit = spanL / 300.0;
                    double dispRatio = (slsLimit > 1e-12) ? rawDisp / slsLimit : double.MaxValue;

                    double avgUtil = ComputeAverageUtilization(finalModel);
                    const double TARGET_UTIL = 0.90;
                    double utilDev = Math.Abs(avgUtil - TARGET_UTIL);
                    if (avgUtil > 1.0)
                        utilDev = (avgUtil - TARGET_UTIL) * 2.0;
                    double maxUtil = ComputeMaxUtilization(finalModel);

                    double rawFeas = (feasResult.VDang + feasResult.VAng + feasResult.VLen + feasResult.VBoundary) / 4.0;
                    rawFeas = Math.Clamp(rawFeas, 0.0, 1.0);

                    double utilObj = utilObjType == 1 ? maxUtil : utilDev;

                    var topoVals = topoMetrics.Select(mt => TopologyMetrics.Compute(shape, mt)).ToList();
                    var shpeVals = shapeMetrics
                        .Select(mt => ShapeMetrics.Compute(shape, mt, shrinkRatio))
                        .ToList();

                    if (numObjectives > 1)
                    {
                        double dispObj = Math.Log(1.0 + Math.Max(0.0, dispRatio));
                        individual.Fitness = dispRatio;
                        individual.ObjectiveValues = new List<double> { dispObj, utilObj };
                        if (numObjectives >= 3)
                            individual.ObjectiveValues.Add(rawFeas);
                    }
                    else
                    {
                        double singleFitness;
                        switch (singleObjType)
                        {
                            case 1: singleFitness = feasResult.TotalViolation; break;
                            case 2: singleFitness = utilDev; break;
                            case 3: singleFitness = maxUtil; break;
                            default:
                                singleFitness = (rawDisp >= double.MaxValue || rawDisp <= double.MinValue)
                                    ? rawDisp : rawDisp * (1.0 + feasResult.TotalViolation);
                                break;
                        }
                        individual.Fitness = singleFitness;
                        individual.ObjectiveValues = new List<double> { dispRatio, utilObj, rawFeas };
                    }

                    individual.TopoValues = topoVals;
                    individual.ShpeValues = shpeVals;
                    individual.Feas = feasResult.TotalViolation;
                    individual.VDang = feasResult.VDang;
                    individual.VAng = feasResult.VAng;
                    individual.VLen = feasResult.VLen;

                    if (finalModel != null && shape.Elems != null && shape.Elems.Count == finalModel.Elem1Ds.Count)
                        SyncShapeSectionsFromModel(shape, finalModel);

                    outcome.EvaluatedPopulation.Add(individual);
                    outcome.Shapes.Add(deepCopyOutputs ? shape.DeepCopy() : shape);
                    outcome.Models.Add(deepCopyOutputs ? finalModel?.DeepCopy() : finalModel);
                }
                catch (Exception ex)
                {
                    individual.Fitness = double.MaxValue;
                    individual.TopoValues = topoMetrics.Select(_ => 0.0).ToList();
                    individual.ShpeValues = shapeMetrics.Select(_ => 0.0).ToList();
                    individual.Feas = 0;
                    individual.VDang = 0;

                    if (numObjectives > 1)
                    {
                        individual.ObjectiveValues = new List<double> { double.MaxValue, double.MaxValue };
                        if (numObjectives >= 3) individual.ObjectiveValues.Add(double.MaxValue);
                    }
                    outcome.EvaluatedPopulation.Add(individual);
                    outcome.Shapes.Add(null);
                    outcome.Models.Add(null);
                    outcome.Warnings.Add(string.Format(
                        "Individual {0} evaluation failed: {1}", i, ex.Message));
                }
            }

            return outcome;
        }

        // ─── Genotype helpers ───────────────────────────────────────────

        private static SG_Genotype CreateGenotypeFromIndividual(GAIndividual individual)
        {
            var intGenes = new List<int>(individual.Chromosome);
            var dGenes = new List<double>(individual.ChromosomeParam);
            return new SG_Genotype(intGenes, dGenes);
        }

        // ─── Boundary / supports / loads ───────────────────────────────

        private static void EnforceBoundaryConstraints(SG_Shape shape, List<SG_Rule> rules)
        {
            var initRule = rules?.OfType<SG_AutoRule_InitShape_3D>().FirstOrDefault();
            if (initRule == null) return;
            var brep = initRule.BoundaryBrep ?? shape?.BoundaryBrep;
            var mesh = initRule.BoundaryMesh ?? shape?.BoundaryMesh;
            BoundaryConstraintUtil.Enforce(shape, brep, mesh, initRule.BoundaryBeamConstraintMode);
        }

        private static void RepairSupportsAndLoads(SG_Shape shape, List<SG_Rule> rules)
        {
            if (shape?.Nodes == null) return;

            shape.Supports ??= new List<SG_Support>();
            shape.Supports.Clear();

            // Treat both endpoint and mid-element nodes as valid load-bearing FEM nodes.
            var elemNodeIds = new HashSet<int>();
            if (shape.Elems != null)
            {
                foreach (var e in shape.Elems)
                {
                    if (e?.Nodes != null)
                        foreach (var n in e.Nodes)
                            if (n != null) elemNodeIds.Add(n.ID);

                    if (e is SG_Elem1D e1d && e1d.MidNodes != null)
                        foreach (var n in e1d.MidNodes)
                            if (n != null) elemNodeIds.Add(n.ID);
                }
            }

            foreach (var nd in shape.Nodes)
            {
                if (nd?.Support == null) continue;
                if (nd.Support.SupportCondition == 0) continue;
                if (!elemNodeIds.Contains(nd.ID)) continue;

                nd.Support.Pt = nd.Pt;
                nd.Support.Node = nd;
                shape.Supports.Add(nd.Support);
            }

            var initRule = rules?.OfType<SG_AutoRule_InitShape_3D>().FirstOrDefault();
            Vector3d loadVec = initRule?.LoadVector ?? Vector3d.Zero;
            Vector3d areaLoadVec = initRule?.AreaLoadVector ?? Vector3d.Zero;

            // If the user supplied loads through the Assembly AND no init-rule load
            // is configured, preserve them as-is. This is the path taken by the new
            // curve+CurveLoad assembly: loads (including line loads) come from the
            // input shape and must survive every GA generation.
            bool hasUserLoads =
                (shape.PointLoads != null && shape.PointLoads.Count > 0) ||
                (shape.LineLoads  != null && shape.LineLoads.Count  > 0);
            if (hasUserLoads)
            {
                shape.PointLoads ??= new List<SG_PointLoad>();
                shape.LineLoads  ??= new List<SG_LineLoad>();
                // Snap user point loads to the nearest existing FEM node so
                // TB_Model.CheckLoads can resolve them to TB nodes.
                var candidateNodes = new List<SG_Node>();
                var seen = new HashSet<int>();
                if (shape.Elems != null)
                {
                    foreach (var e in shape.Elems)
                    {
                        if (e?.Nodes != null)
                        {
                            foreach (var n in e.Nodes)
                            {
                                if (n == null || !seen.Add(n.ID)) continue;
                                candidateNodes.Add(n);
                            }
                        }

                        if (e is SG_Elem1D e1d && e1d.MidNodes != null)
                        {
                            foreach (var n in e1d.MidNodes)
                            {
                                if (n == null || !seen.Add(n.ID)) continue;
                                candidateNodes.Add(n);
                            }
                        }
                    }
                }

                if (candidateNodes.Count > 0)
                {
                    for (int i = shape.PointLoads.Count - 1; i >= 0; i--)
                    {
                        var pl = shape.PointLoads[i];
                        if (pl == null)
                        {
                            shape.PointLoads.RemoveAt(i);
                            continue;
                        }

                        SG_Node closest = null;
                        double best = double.MaxValue;
                        foreach (var n in candidateNodes)
                        {
                            double d = pl.Position.DistanceToSquared(n.Pt);
                            if (d < best)
                            {
                                best = d;
                                closest = n;
                            }
                        }

                        if (closest == null)
                        {
                            shape.PointLoads.RemoveAt(i);
                            continue;
                        }

                        pl.Position = closest.Pt;
                    }
                }
                return;
            }

            shape.PointLoads ??= new List<SG_PointLoad>();
            shape.PointLoads.Clear();

            if (areaLoadVec.Length > 1e-12 && initRule != null)
            {
                ApplyVoronoiAreaLoads(shape, initRule, elemNodeIds, areaLoadVec, loadVec);
            }
            else
            {
                if (loadVec.Length < 1e-12) loadVec = new Vector3d(0, 0, -100);
                foreach (var nd in shape.Nodes)
                {
                    if (nd == null || !elemNodeIds.Contains(nd.ID)) continue;
                    shape.PointLoads.Add(new SG_PointLoad(loadVec, Vector3d.Zero, nd.Pt));
                }
            }
        }

        private static void ApplyVoronoiAreaLoads(
            SG_Shape shape, SG_AutoRule_InitShape_3D initRule,
            HashSet<int> elemEndpoints, Vector3d areaLoadVec, Vector3d fallbackLoadVec)
        {
            var bb = initRule.DesignSpace;
            double xMin = bb.Min.X, xMax = bb.Max.X;
            double yMin = bb.Min.Y, yMax = bb.Max.Y;

            var seedNodes = VoronoiAreaLoadUtil.CollectAreaLoadVoronoiSeedNodes(shape, bb);
            if (seedNodes.Count == 0) return;
            var seeds = seedNodes.Select(n => (n.ID, n.Pt.X, n.Pt.Y)).ToList();
            var voronoiAreas = ComputeVoronoiAreas(seeds, xMin, xMax, yMin, yMax);

            foreach (var n in seedNodes)
            {
                if (!voronoiAreas.TryGetValue(n.ID, out double area)) continue;
                shape.PointLoads.Add(new SG_PointLoad(area * areaLoadVec, Vector3d.Zero, n.Pt));
            }
        }

        private static Dictionary<int, double> ComputeVoronoiAreas(
            List<(int nodeId, double x, double y)> tips,
            double xMin, double xMax, double yMin, double yMax,
            int gridRes = 100)
        {
            double totalW = xMax - xMin;
            double totalH = yMax - yMin;
            if (totalW < 1e-9 || totalH < 1e-9)
                return tips.ToDictionary(t => t.nodeId, _ => 0.0);

            double cellArea = (totalW * totalH) / (gridRes * gridRes);
            var counts = new Dictionary<int, int>();
            foreach (var t in tips) counts[t.nodeId] = 0;

            double dx = totalW / gridRes;
            double dy = totalH / gridRes;

            for (int iy = 0; iy < gridRes; iy++)
            {
                double gy = yMin + (iy + 0.5) * dy;
                for (int ix = 0; ix < gridRes; ix++)
                {
                    double gx = xMin + (ix + 0.5) * dx;
                    double bestDistSq = double.MaxValue;
                    int bestId = tips[0].nodeId;
                    foreach (var t in tips)
                    {
                        double ddx = gx - t.x;
                        double ddy = gy - t.y;
                        double dSq = ddx * ddx + ddy * ddy;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestId = t.nodeId;
                        }
                    }
                    counts[bestId]++;
                }
            }
            return counts.ToDictionary(kv => kv.Key, kv => kv.Value * cellArea);
        }

        private static void ApplySelfWeightLoads(SG_Shape shape, Vector3d gravityDir)
        {
            if (shape == null || shape.Nodes == null || shape.Elems == null) return;

            var nodalForces = new Dictionary<int, double>();
            foreach (var node in shape.Nodes)
                nodalForces[node.ID] = 0.0;

            foreach (var elem in shape.Elems)
            {
                if (!(elem is SG_Elem1D elem1d)) continue;
                double length = elem1d.Crv != null ? elem1d.Crv.GetLength() : elem1d.Ln.Length;
                double areaMm2 = elem1d.CrossSection?.Area ?? 0.0;
                double density = elem1d.CrossSection?.Material?.Density ?? 0.0;
                if (areaMm2 <= 0 || density <= 0 || length <= 0) continue;

                double areaM2 = areaMm2 * 1e-6;
                double weightKN = length * areaM2 * density;
                double halfWeight = weightKN * 0.5;

                if (elem1d.Nodes != null && elem1d.Nodes.Length >= 2
                    && elem1d.Nodes[0] != null && elem1d.Nodes[1] != null)
                {
                    int id0 = elem1d.Nodes[0].ID;
                    int id1 = elem1d.Nodes[1].ID;
                    if (nodalForces.ContainsKey(id0)) nodalForces[id0] += halfWeight;
                    if (nodalForces.ContainsKey(id1)) nodalForces[id1] += halfWeight;
                }
            }

            shape.PointLoads ??= new List<SG_PointLoad>();
            foreach (var node in shape.Nodes)
            {
                double force = nodalForces.ContainsKey(node.ID) ? nodalForces[node.ID] : 0.0;
                if (force <= 0) continue;
                shape.PointLoads.Add(new SG_PointLoad(gravityDir * force, Vector3d.Zero, node.Pt));
            }
        }

        // ─── Displacement / span / utilization ─────────────────────────

        private static double CalculateMaxNodalDisplacement(TB_Model model)
        {
            if (model == null || model.Nodes == null || model.Nodes.Count == 0)
                return double.MaxValue;

            double maxDisplacement = 0.0;
            foreach (var node in model.Nodes)
            {
                if (node.Disps == null || node.Disps.Count == 0) continue;
                double[] disp = node.Disps.Last();
                if (disp == null || disp.Length < 3) continue;
                double d = Math.Sqrt(disp[0] * disp[0] + disp[1] * disp[1] + disp[2] * disp[2]);
                if (d > maxDisplacement) maxDisplacement = d;
            }
            return maxDisplacement == 0.0 ? double.MaxValue : maxDisplacement;
        }

        private static double ComputeSpanL(TB_Model model)
        {
            if (model?.Sups == null || model.Sups.Count == 0) return 1.0;

            var supPts = model.Sups.Select(s => s.Pt).ToList();
            double maxSpan = 0.0;

            if (supPts.Count >= 2)
            {
                for (int i = 0; i < supPts.Count; i++)
                    for (int j = i + 1; j < supPts.Count; j++)
                    {
                        double d = supPts[i].DistanceTo(supPts[j]);
                        if (d > maxSpan) maxSpan = d;
                    }
            }

            if (maxSpan < 1e-9 && model.Nodes != null)
            {
                foreach (var node in model.Nodes)
                    foreach (var sp in supPts)
                    {
                        double d = node.Pt.DistanceTo(sp);
                        if (d > maxSpan) maxSpan = d;
                    }
            }

            return maxSpan > 1e-9 ? maxSpan : 1.0;
        }

        private static double ComputeAverageUtilization(TB_Model model)
        {
            if (model?.Elem1Ds == null || model.Elem1Ds.Count == 0) return 0.0;

            double sum = 0.0;
            int count = 0;
            foreach (var elem in model.Elem1Ds)
            {
                double u = ComputeElementUtilization(model, elem);
                if (u < double.MaxValue)
                {
                    sum += u;
                    count++;
                }
            }
            return count > 0 ? sum / count : 0.0;
        }

        private static double ComputeMaxUtilization(TB_Model model)
        {
            if (model?.Elem1Ds == null || model.Elem1Ds.Count == 0) return 0.0;
            double maxU = 0.0;
            foreach (var elem in model.Elem1Ds)
            {
                double u = ComputeElementUtilization(model, elem);
                if (u < double.MaxValue && u > maxU) maxU = u;
            }
            return maxU;
        }

        private static double ComputeElementUtilization(TB_Model model, TB_Element_1D elem)
        {
            const double GAMMA_M0 = 1.0;
            double fy = elem.Sec.Mat.Fy;
            double E = elem.Sec.Mat.E;

            double N_Rd = elem.Sec.Area * fy / GAMMA_M0;
            double My_Rd = elem.Sec.Wy * fy / GAMMA_M0 * 1e-3;
            double Mz_Rd = elem.Sec.Wz * fy / GAMMA_M0 * 1e-3;
            if (N_Rd <= 0 || My_Rd <= 0 || Mz_Rd <= 0) return double.MaxValue;

            double iy = Math.Sqrt(elem.Sec.Iy / elem.Sec.Area);
            double iz = Math.Sqrt(elem.Sec.Iz / elem.Sec.Area);

            double Lcr = elem.Line.Length * 1000.0;
            double alphaBuck = fy < 460 ? 0.21 : 0.13;
            double lambda1 = Math.PI * Math.Sqrt(E / fy);

            double chiY = 1.0, chiZ = 1.0;
            if (iy > 0)
            {
                double lb = (Lcr / iy) / lambda1;
                double phi = 0.5 * (1 + alphaBuck * (lb - 0.2) + lb * lb);
                double d = phi * phi - lb * lb;
                chiY = d > 0 ? Math.Min(1.0 / (phi + Math.Sqrt(d)), 1.0) : 0.01;
                chiY = Math.Max(chiY, 0.01);
            }
            if (iz > 0)
            {
                double lb = (Lcr / iz) / lambda1;
                double phi = 0.5 * (1 + alphaBuck * (lb - 0.2) + lb * lb);
                double d = phi * phi - lb * lb;
                chiZ = d > 0 ? Math.Min(1.0 / (phi + Math.Sqrt(d)), 1.0) : 0.01;
                chiZ = Math.Max(chiZ, 0.01);
            }
            double chi = Math.Min(chiY, chiZ);

            double maxUtil = 0.0;
            if (model.LCs == null) return maxUtil;

            foreach (int lc in model.LCs)
            {
                int lcId = Array.IndexOf(model.LCs, lc);
                double[] F = elem.Calc_Forces(lcId);

                double N_Ed = Math.Max(Math.Abs(F[0]), Math.Abs(F[6]));
                double My_Ed = Math.Max(Math.Abs(F[4]), Math.Abs(F[10]));
                double Mz_Ed = Math.Max(Math.Abs(F[5]), Math.Abs(F[11]));

                double utilCS = N_Ed / N_Rd + My_Ed / My_Rd + Mz_Ed / Mz_Rd;
                double util = utilCS;

                double N_c = Math.Min(F[0], F[6]);
                if (N_c < 0)
                {
                    double N_bRd = chi * elem.Sec.Area * fy / GAMMA_M0;
                    double utilBuck = Math.Abs(N_c) / N_bRd + My_Ed / My_Rd + Mz_Ed / Mz_Rd;
                    util = Math.Max(utilCS, utilBuck);
                }
                if (util > maxUtil) maxUtil = util;
            }

            return maxUtil;
        }

        // ─── Cross-section optimization (Rect FSD + SHS) ───────────────

        private struct PrecomputedSec
        {
            public double Area, Wy, Wz, Iy, Iz;
        }

        private static (double N, double My, double Mz, double Ncomp) GetElementMaxForces(
            TB_Model model, TB_Element_1D elem)
        {
            double maxN = 0, maxMy = 0, maxMz = 0;
            double maxNcomp = 0;
            if (model.LCs == null) return (0, 0, 0, 0);

            foreach (int lc in model.LCs)
            {
                int lcId = Array.IndexOf(model.LCs, lc);
                double[] F = elem.Calc_Forces(lcId);
                maxN = Math.Max(maxN, Math.Max(Math.Abs(F[0]), Math.Abs(F[6])));
                maxMy = Math.Max(maxMy, Math.Max(Math.Abs(F[4]), Math.Abs(F[10])));
                maxMz = Math.Max(maxMz, Math.Max(Math.Abs(F[5]), Math.Abs(F[11])));
                double nComp = Math.Min(F[0], F[6]);
                if (nComp < maxNcomp) maxNcomp = nComp;
            }
            return (maxN, maxMy, maxMz, maxNcomp);
        }

        private static double ComputeUtilForSection(
            (double N, double My, double Mz, double Ncomp) forces,
            PrecomputedSec sec, double fy, double E, double elemLengthM)
        {
            double nRd = sec.Area * fy;
            double myRd = sec.Wy * fy * 1e-3;
            double mzRd = sec.Wz * fy * 1e-3;
            if (nRd <= 0 || myRd <= 0 || mzRd <= 0) return double.MaxValue;

            double utilCS = forces.N / nRd + forces.My / myRd + forces.Mz / mzRd;
            if (forces.Ncomp >= 0) return utilCS;

            double Lcr = elemLengthM * 1000.0;
            double alphaBuck = fy < 460 ? 0.21 : 0.13;
            double lambda1 = Math.PI * Math.Sqrt(E / fy);
            double chi = ComputeChiMin(sec.Area, sec.Iy, sec.Iz, Lcr, alphaBuck, lambda1);

            double N_Ed = Math.Abs(forces.Ncomp);
            double N_bRd = chi * sec.Area * fy;
            double utilBuckling = N_Ed / N_bRd + forces.My / myRd + forces.Mz / mzRd;
            return Math.Max(utilCS, utilBuckling);
        }

        private static double ComputeChiMin(
            double area, double iy_mm4, double iz_mm4,
            double lcrMm, double alphaBuck, double lambda1)
        {
            double chiY = 1.0, chiZ = 1.0;
            double riy = area > 0 && iy_mm4 > 0 ? Math.Sqrt(iy_mm4 / area) : 0;
            double riz = area > 0 && iz_mm4 > 0 ? Math.Sqrt(iz_mm4 / area) : 0;

            if (riy > 0)
            {
                double lb = (lcrMm / riy) / lambda1;
                double ph = 0.5 * (1 + alphaBuck * (lb - 0.2) + lb * lb);
                double d = ph * ph - lb * lb;
                chiY = d > 0 ? Math.Min(1.0 / (ph + Math.Sqrt(d)), 1.0) : 0.01;
                chiY = Math.Max(chiY, 0.01);
            }
            if (riz > 0)
            {
                double lb = (lcrMm / riz) / lambda1;
                double ph = 0.5 * (1 + alphaBuck * (lb - 0.2) + lb * lb);
                double d = ph * ph - lb * lb;
                chiZ = d > 0 ? Math.Min(1.0 / (ph + Math.Sqrt(d)), 1.0) : 0.01;
                chiZ = Math.Max(chiZ, 0.01);
            }
            return Math.Min(chiY, chiZ);
        }

        private static int FindBestSectionIdx(
            (double N, double My, double Mz, double Ncomp) forces,
            PrecomputedSec[] catalog, double fy, double E,
            double elemLengthM, double targetUtil)
        {
            int n = catalog.Length;
            int bestIdx = n - 1;
            double bestDist = double.MaxValue;

            for (int i = 0; i < n; i++)
            {
                double u = ComputeUtilForSection(forces, catalog[i], fy, E, elemLengthM);
                if (u > 1.0) continue;
                double dist = Math.Abs(u - targetUtil);
                if (dist < bestDist) { bestDist = dist; bestIdx = i; }
            }
            return bestIdx;
        }

        private static TB_Model OptimizeCrossSections_Rect(TB_Model solvedModel, int maxIter)
        {
            if (solvedModel == null || solvedModel.Elem1Ds == null || solvedModel.Elem1Ds.Count == 0)
                return solvedModel;

            const int NUM_SIZES = 20;
            const double STEP_MM = 50.0;
            const double TARGET_UTIL = 0.90;

            var catalog = new PrecomputedSec[NUM_SIZES];
            for (int i = 0; i < NUM_SIZES; i++)
            {
                double dim = (i + 1) * STEP_MM;
                double inertia = Math.Pow(dim, 4) / 12.0;
                catalog[i] = new PrecomputedSec
                {
                    Area = dim * dim,
                    Wy = dim * dim * dim / 6.0,
                    Wz = dim * dim * dim / 6.0,
                    Iy = inertia,
                    Iz = inertia
                };
            }

            int elemCount = solvedModel.Elem1Ds.Count;
            double[] elemLengths = solvedModel.Elem1Ds.Select(e => e.Line.Length).ToArray();
            int[] secIdx = new int[elemCount];

            for (int e = 0; e < elemCount; e++)
            {
                double fy = solvedModel.Elem1Ds[e].Sec.Mat.Fy;
                double E = solvedModel.Elem1Ds[e].Sec.Mat.E;
                var forces = GetElementMaxForces(solvedModel, solvedModel.Elem1Ds[e]);
                secIdx[e] = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], TARGET_UTIL);
            }

            for (int iter = 0; iter < maxIter; iter++)
            {
                TB_Model model = RebuildModelWithRectSections(solvedModel, secIdx, STEP_MM);
                var slv = new SolveLS(ref model);
                model = slv.Mdl;

                int[] newIdx = new int[elemCount];
                int totalDelta = 0;
                double alpha = 0.7 + 0.25 * Math.Min(1.0, (double)iter / 10.0);

                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                    double E = model.Elem1Ds[e].Sec.Mat.E;
                    var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                    int target = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], TARGET_UTIL);
                    int damped = (int)Math.Round(secIdx[e] + alpha * (target - secIdx[e]));
                    newIdx[e] = Math.Clamp(damped, 0, NUM_SIZES - 1);
                    totalDelta += Math.Abs(newIdx[e] - secIdx[e]);
                }

                secIdx = newIdx;
                if (totalDelta == 0) break;
            }

            for (int safety = 0; safety < 5; safety++)
            {
                TB_Model model = RebuildModelWithRectSections(solvedModel, secIdx, STEP_MM);
                var slv = new SolveLS(ref model);
                model = slv.Mdl;

                bool anyBumped = false;
                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double util = ComputeElementUtilization(model, model.Elem1Ds[e]);
                    if (util > 1.0)
                    {
                        double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                        double E = model.Elem1Ds[e].Sec.Mat.E;
                        var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                        int target = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], 1.0);
                        if (target > secIdx[e]) { secIdx[e] = target; anyBumped = true; }
                        else if (secIdx[e] < NUM_SIZES - 1) { secIdx[e]++; anyBumped = true; }
                    }
                }
                if (!anyBumped) break;
            }

            TB_Model finalModel = RebuildModelWithRectSections(solvedModel, secIdx, STEP_MM);
            var finalSlv = new SolveLS(ref finalModel);
            return finalSlv.Mdl;
        }

        private static TB_Model RebuildModelWithRectSections(TB_Model template, int[] sectionIdx, double stepMm)
        {
            var newElems = new List<TB_Element_1D>();
            for (int i = 0; i < template.Elem1Ds.Count; i++)
            {
                TB_Element_1D orig = template.Elem1Ds[i];
                double dim = (sectionIdx[i] + 1) * stepMm;
                string tag = string.Format("Rect_{0}x{0}", dim);
                Section_Rect sec = new Section_Rect(orig.Sec.Mat, tag, dim, dim);
                newElems.Add(new TB_Element_1D(orig.Line, orig.Tag, sec, orig.Vz, orig.Line.Length));
            }
            return new TB_Model(newElems, template.Sups, template.Loads);
        }

        private static TB_Model OptimizeCrossSections_SHS(TB_Model solvedModel, int maxIter)
        {
            if (solvedModel == null || solvedModel.Elem1Ds == null || solvedModel.Elem1Ds.Count == 0)
                return solvedModel;

            var combos = SHS_Catalog.AllCombinations();
            if (combos.Count == 0) return solvedModel;

            var sortedRaw = combos
                .Select(c =>
                {
                    double s = c.Size, t = c.T;
                    double bi = s - 2 * t;
                    double area = s * s - bi * bi;
                    double iy = (Math.Pow(s, 4) - Math.Pow(bi, 4)) / 12.0;
                    double wy = 2.0 * iy / s;
                    return (c.Size, c.T, Area: area, Wy: wy, Iy: iy);
                })
                .OrderBy(x => x.Area)
                .ToList();

            var sortedCombos = sortedRaw.Select(x => (x.Size, x.T, x.Area)).ToList();
            var catalog = sortedRaw
                .Select(x => new PrecomputedSec { Area = x.Area, Wy = x.Wy, Wz = x.Wy, Iy = x.Iy, Iz = x.Iy })
                .ToArray();

            int numOptions = catalog.Length;
            int elemCount = solvedModel.Elem1Ds.Count;
            double[] elemLengths = solvedModel.Elem1Ds.Select(e => e.Line.Length).ToArray();

            const double TARGET_UTIL = 0.90;
            int[] secIdx = new int[elemCount];

            for (int e = 0; e < elemCount; e++)
            {
                double fy = solvedModel.Elem1Ds[e].Sec.Mat.Fy;
                double E = solvedModel.Elem1Ds[e].Sec.Mat.E;
                var forces = GetElementMaxForces(solvedModel, solvedModel.Elem1Ds[e]);
                secIdx[e] = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], TARGET_UTIL);
            }

            for (int iter = 0; iter < maxIter; iter++)
            {
                TB_Model model = RebuildModelWithSHSSections(solvedModel, secIdx, sortedCombos);
                var slv = new SolveLS(ref model);
                model = slv.Mdl;

                int[] newIdx = new int[elemCount];
                int totalDelta = 0;
                double alpha = 0.7 + 0.25 * Math.Min(1.0, (double)iter / 10.0);

                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                    double E = model.Elem1Ds[e].Sec.Mat.E;
                    var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                    int target = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], TARGET_UTIL);
                    int damped = (int)Math.Round(secIdx[e] + alpha * (target - secIdx[e]));
                    newIdx[e] = Math.Clamp(damped, 0, numOptions - 1);
                    totalDelta += Math.Abs(newIdx[e] - secIdx[e]);
                }

                secIdx = newIdx;
                if (totalDelta == 0) break;
            }

            for (int safety = 0; safety < 5; safety++)
            {
                TB_Model model = RebuildModelWithSHSSections(solvedModel, secIdx, sortedCombos);
                var slv = new SolveLS(ref model);
                model = slv.Mdl;

                bool anyBumped = false;
                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double util = ComputeElementUtilization(model, model.Elem1Ds[e]);
                    if (util > 1.0)
                    {
                        double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                        double E = model.Elem1Ds[e].Sec.Mat.E;
                        var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                        int target = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], 1.0);
                        if (target > secIdx[e]) { secIdx[e] = target; anyBumped = true; }
                        else if (secIdx[e] < numOptions - 1) { secIdx[e]++; anyBumped = true; }
                    }
                }
                if (!anyBumped) break;
            }

            TB_Model finalModel = RebuildModelWithSHSSections(solvedModel, secIdx, sortedCombos);
            var finalSlv = new SolveLS(ref finalModel);
            return finalSlv.Mdl;
        }

        private static TB_Model RebuildModelWithSHSSections(
            TB_Model template, int[] secIdx, List<(int Size, int T, double Area)> sortedCombos)
        {
            var newElems = new List<TB_Element_1D>();
            for (int i = 0; i < template.Elem1Ds.Count; i++)
            {
                TB_Element_1D orig = template.Elem1Ds[i];
                int idx = Math.Clamp(secIdx[i], 0, sortedCombos.Count - 1);
                var combo = sortedCombos[idx];
                string tag = string.Format("SHS_{0}x{0}x{1}", combo.Size, combo.T);
                Section_RHS sec = new Section_RHS(orig.Sec.Mat, tag, combo.Size, combo.Size, combo.T, combo.T);
                newElems.Add(new TB_Element_1D(orig.Line, orig.Tag, sec, orig.Vz, orig.Line.Length));
            }
            return new TB_Model(newElems, template.Sups, template.Loads);
        }

        private static void SyncShapeSectionsFromModel(SG_Shape shape, TB_Model model)
        {
            if (shape?.Elems == null || model?.Elem1Ds == null || shape.Elems.Count != model.Elem1Ds.Count)
                return;
            for (int i = 0; i < shape.Elems.Count; i++)
            {
                if (!(shape.Elems[i] is SG_Elem1D sgElem) || model.Elem1Ds[i]?.Sec == null) continue;
                var sec = model.Elem1Ds[i].Sec;
                double area = sec.Area;
                if (area <= 0) continue;
                double dim = Math.Sqrt(area);
                var mat = sgElem.CrossSection?.Material ?? SH_Material_Isotrop.Default_Material();
                var newRect = new SH_CrossSection_Rectangle(dim, dim);
                newRect.Material = mat;
                sgElem.CrossSection = newRect;
            }
        }
    }
}
