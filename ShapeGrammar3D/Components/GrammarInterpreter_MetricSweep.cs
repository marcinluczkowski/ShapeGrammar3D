using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Sweeps multiple combinations of (Topology metrics, Shape metrics) and
    /// runs an NSGA-II GA (3 objectives, gravity -Z, optional cross-section
    /// optimization) several times per combination so the user can validate
    /// the convergence rate per objective and inspect top-1 individuals
    /// per cluster.
    ///
    /// Combination input modes:
    ///   - Provide Topo Combos / Shape Combos as DataTree (one branch per
    ///     combination) for full control.
    ///   - Otherwise provide Topo Candidates / Shape Candidates lists and the
    ///     component will generate all singleton pairs (|T| * |S| combos).
    ///
    /// Geometry layout: rows = clusters, columns = combination index.
    /// </summary>
    public class GrammarInterpreter_MetricSweep : GH_Component
    {
        public GrammarInterpreter_MetricSweep()
          : base("Grammar Interpreter Metric Sweep", "GI_MetricSweep",
              "Runs NSGA-II (3 obj, gravity -Z) for many (topo, shape) metric combinations with multiple reruns. Outputs top-1 per cluster per combo and per-objective convergence.",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "Initial SG_Shape (assembly).", GH_ParamAccess.item);              // 0
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "List of automatic shape grammar rules.", GH_ParamAccess.list); // 1
            pManager.AddBooleanParameter("Reset", "Reset", "Reset and rerun the sweep.", GH_ParamAccess.item, false);               // 2
            pManager.AddParameter(new Param_GrammarInterpreterSettings(), "Settings", "Settings",
                "Optional base settings (mutation, crossover, elite, feasibility weights). Per-combo overrides: NumObjectives=3, GravityDir=(0,0,-1), TopologyMetrics, ShapeMetrics.",
                GH_ParamAccess.item);                                                                                                // 3
            pManager.AddIntegerParameter("Topo Combos", "TopoTree",
                "DataTree<int>: one branch per combination, branch values = topology metric IDs. Optional; if empty falls back to Topo Candidates.",
                GH_ParamAccess.tree);                                                                                                // 4
            pManager.AddIntegerParameter("Shape Combos", "ShapeTree",
                "DataTree<int>: one branch per combination (must align with Topo Combos branch count).",
                GH_ParamAccess.tree);                                                                                                // 5
            pManager.AddIntegerParameter("Topo Candidates", "TopoCand",
                "List<int>: candidate topology metric IDs (used when Topo Combos tree is empty; produces all singleton pairs with Shape Candidates).",
                GH_ParamAccess.list);                                                                                                // 6
            pManager.AddIntegerParameter("Shape Candidates", "ShapeCand",
                "List<int>: candidate shape metric IDs.",
                GH_ParamAccess.list);                                                                                                // 7
            pManager.AddIntegerParameter("Population Size", "Pop", "GA population size per rerun.", GH_ParamAccess.item, 100);      // 8
            pManager.AddIntegerParameter("Generations", "Gen", "GA generations per rerun.", GH_ParamAccess.item, 20);                // 9
            pManager.AddIntegerParameter("Clusters", "Clust", "Number of clusters used by SG_GA.ClusterPopulation.", GH_ParamAccess.item, 5); // 10
            pManager.AddIntegerParameter("Reruns", "Reruns", "Number of reruns per combination (averaged for convergence).", GH_ParamAccess.item, 5); // 11
            pManager.AddIntegerParameter("CroSec Opt", "CSOpt",
                "Cross-section optimization mode: 0=off, 1=Rect FSD, 2=SHS catalog. Modes >2 fall back to Rect FSD.",
                GH_ParamAccess.item, 1);                                                                                            // 12
            pManager.AddNumberParameter("Grid Spacing X", "dX", "Horizontal grid spacing between combinations [m].", GH_ParamAccess.item, 30.0); // 13
            pManager.AddNumberParameter("Grid Spacing Y", "dY", "Vertical grid spacing between clusters [m].", GH_ParamAccess.item, 30.0);       // 14

            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info",
                "Sweep summary table: combination -> metrics -> displacement (mean / min / max over reruns) and per-objective convergence rate.",
                GH_ParamAccess.item);                                                                                                // 0
            pManager.AddLineParameter("Geometry", "Lines",
                "Top-1 individual per cluster (last generation, best rerun). Tree path {combo;cluster}, pre-translated to grid (X = combo, -Y = cluster).",
                GH_ParamAccess.tree);                                                                                                // 1
            pManager.AddTextParameter("Combo Labels", "Labels",
                "Per-combination label: 'C{i}: T[..]+S[..]'. Tree path {combo}.",
                GH_ParamAccess.tree);                                                                                                // 2
            pManager.AddNumberParameter("Convergence", "Conv",
                "Per-objective best objective value per generation, averaged across reruns. Tree path {combo;objective}, branch = generations (0..G-1).",
                GH_ParamAccess.tree);                                                                                                // 3
            pManager.AddNumberParameter("Disp Per Rerun", "DispR",
                "Best displacement-ratio per rerun. Tree path {combo}, branch = reruns.",
                GH_ParamAccess.tree);                                                                                                // 4
            pManager.AddPointParameter("Cell Anchors", "Anchors",
                "Origin point of each (combo, cluster) cell. Useful for placing labels above each grid cell.",
                GH_ParamAccess.tree);                                                                                                // 5
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ── Inputs ──────────────────────────────────────────────
            SG_Shape iniShape = null;
            var rules = new List<SG_Rule>();
            bool reset = false;

            if (!DA.GetData(0, ref iniShape) || iniShape == null) return;
            if (!DA.GetDataList(1, rules)) return;
            DA.GetData(2, ref reset);

            GH_GrammarInterpreterSettings ghSettings = null;
            DA.GetData(3, ref ghSettings);

            DA.GetDataTree(4, out GH_Structure<GH_Integer> topoTree);
            DA.GetDataTree(5, out GH_Structure<GH_Integer> shapeTree);

            var topoCand = new List<int>();
            var shapeCand = new List<int>();
            DA.GetDataList(6, topoCand);
            DA.GetDataList(7, shapeCand);

            int popSize = 100, generations = 20, clusters = 5, reruns = 5, croSecMode = 1;
            double dx = 30.0, dy = 30.0;
            DA.GetData(8, ref popSize);
            DA.GetData(9, ref generations);
            DA.GetData(10, ref clusters);
            DA.GetData(11, ref reruns);
            DA.GetData(12, ref croSecMode);
            DA.GetData(13, ref dx);
            DA.GetData(14, ref dy);

            popSize = Math.Max(1, popSize);
            generations = Math.Max(1, generations);
            clusters = Math.Max(1, clusters);
            reruns = Math.Max(1, reruns);

            // ── Resolve metric combinations ─────────────────────────
            var combos = ResolveCombinations(topoTree, shapeTree, topoCand, shapeCand, out string comboError);
            if (combos.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, comboError ?? "No metric combinations resolved.");
                return;
            }

            // ── Base settings (overridden per-combo) ────────────────
            GrammarInterpreterSettings baseSettings = ghSettings?.Value != null
                ? CloneSettings(ghSettings.Value)
                : new GrammarInterpreterSettings();
            baseSettings.PopulationSize = popSize;
            baseSettings.Generations = generations;
            baseSettings.Clusters = clusters;
            baseSettings.NumObjectives = 3;
            baseSettings.GravityDir = new Vector3d(0, 0, -1);
            baseSettings.CroSecOpt = Math.Clamp(croSecMode, 0, 2);
            baseSettings.SelfWeight = baseSettings.SelfWeight; // honor user choice
            baseSettings.Sanitize();
            // Sanitize() unitizes GravityDir but does not change the direction.

            var feas = new FeasibilitySettings
            {
                WDang = baseSettings.DanglingWeight,
                WAng = baseSettings.AngleWeight,
                WLen = baseSettings.LengthWeight,
                WIntersect = baseSettings.IntersectionWeight,
                WRepet = baseSettings.RepetWeight,
                WDup = baseSettings.DuplicateWeight,
                AngleMinDeg = baseSettings.AngleMinDeg,
                AngleOptDeg = baseSettings.AngleOptDeg,
                LenTooShort = baseSettings.LenTooShort,
                LenOptLow = baseSettings.LenOptLow,
                LenOptHigh = baseSettings.LenOptHigh,
                LenTooLong = baseSettings.LenTooLong
            };

            var orderedRules = StructuralEvaluator.EnsureInitShapeFirst(rules);

            // ── Output trees ────────────────────────────────────────
            var linesTree = new GH_Structure<GH_Line>();
            var labelsTree = new GH_Structure<GH_String>();
            var convergenceTree = new GH_Structure<GH_Number>();
            var dispTree = new GH_Structure<GH_Number>();
            var anchorTree = new GH_Structure<GH_Point>();

            var infoBuilder = new StringBuilder();
            infoBuilder.AppendLine(string.Format(
                "GI_MetricSweep: {0} combinations × {1} reruns × {2} gen × pop {3} (clusters={4}, croSec={5})",
                combos.Count, reruns, generations, popSize, clusters, croSecMode));
            infoBuilder.AppendLine("Combo | Topo | Shape | DispRatio (mean / min / max) | Conv slope per obj");
            infoBuilder.AppendLine(new string('-', 100));

            // ── Sweep loop ──────────────────────────────────────────
            for (int ci = 0; ci < combos.Count; ci++)
            {
                var (topoIds, shapeIds) = combos[ci];
                string comboLabel = string.Format("C{0}: T[{1}]+S[{2}]",
                    ci,
                    string.Join(",", topoIds),
                    string.Join(",", shapeIds));
                labelsTree.Append(new GH_String(comboLabel), new GH_Path(ci));

                // Per-rerun storage
                var bestDispPerRerun = new List<double>();
                // Per-generation per-objective best (averaged across reruns)
                int numObj = 3;
                var convergenceAccum = new double[generations][];
                var convergenceCounts = new int[generations][];
                for (int g = 0; g < generations; g++)
                {
                    convergenceAccum[g] = new double[numObj];
                    convergenceCounts[g] = new int[numObj];
                }

                List<GAIndividual> bestRerunFinalPop = null;
                List<SG_Shape> bestRerunFinalShapes = null;
                double bestRerunDisp = double.MaxValue;

                for (int r = 0; r < reruns; r++)
                {
                    // Per-combo settings clone with the metric subsets
                    var settings = CloneSettings(baseSettings);
                    settings.TopologyMetrics = new List<int>(topoIds);
                    settings.ShapeMetrics = new List<int>(shapeIds);
                    settings.Sanitize();

                    var (finalPop, finalShapes, perGenBest) = RunOneGA(
                        iniShape, orderedRules, settings, feas);

                    // Track convergence: for each generation accumulate min objective values
                    for (int g = 0; g < perGenBest.Count && g < generations; g++)
                    {
                        for (int o = 0; o < numObj && o < perGenBest[g].Length; o++)
                        {
                            double v = perGenBest[g][o];
                            if (double.IsFinite(v))
                            {
                                convergenceAccum[g][o] += v;
                                convergenceCounts[g][o]++;
                            }
                        }
                    }

                    // Best displacement of this rerun (Fitness == dispRatio in MO mode)
                    double bestDisp = finalPop
                        .Where(i => double.IsFinite(i.Fitness) && i.Fitness < double.MaxValue * 0.5)
                        .Select(i => i.Fitness)
                        .DefaultIfEmpty(double.MaxValue)
                        .Min();
                    bestDispPerRerun.Add(bestDisp);
                    dispTree.Append(new GH_Number(bestDisp), new GH_Path(ci));

                    if (bestDisp < bestRerunDisp)
                    {
                        bestRerunDisp = bestDisp;
                        bestRerunFinalPop = finalPop;
                        bestRerunFinalShapes = finalShapes;
                    }
                }

                // ── Write geometry for the best rerun (top-1 per cluster) ──
                if (bestRerunFinalPop != null && bestRerunFinalShapes != null)
                {
                    var grouped = bestRerunFinalPop
                        .Select((ind, idx) => new { ind, idx })
                        .Where(x => x.ind != null
                                 && double.IsFinite(x.ind.Fitness)
                                 && x.ind.Fitness < double.MaxValue * 0.5
                                 && x.idx < bestRerunFinalShapes.Count
                                 && bestRerunFinalShapes[x.idx] != null)
                        .GroupBy(x => x.ind.ClustGrp)
                        .OrderBy(g => g.Key);

                    foreach (var grp in grouped)
                    {
                        int clusterId = grp.Key;
                        var top = grp.OrderBy(x => x.ind.Fitness).First();
                        var shape = bestRerunFinalShapes[top.idx];

                        var anchor = new Point3d(ci * dx, -clusterId * dy, 0);
                        anchorTree.Append(new GH_Point(anchor), new GH_Path(ci, clusterId));

                        var path = new GH_Path(ci, clusterId);
                        if (shape.Elems != null)
                        {
                            var offset = new Vector3d(anchor);
                            foreach (var e in shape.Elems)
                            {
                                if (e is SG_Elem1D e1d)
                                {
                                    var ln = new Line(e1d.Ln.From + offset, e1d.Ln.To + offset);
                                    linesTree.Append(new GH_Line(ln), path);
                                }
                            }
                        }
                    }
                }

                // ── Write convergence series ──
                var slopes = new double[numObj];
                for (int o = 0; o < numObj; o++)
                {
                    var path = new GH_Path(ci, o);
                    var series = new List<double>();
                    for (int g = 0; g < generations; g++)
                    {
                        double v = convergenceCounts[g][o] > 0
                            ? convergenceAccum[g][o] / convergenceCounts[g][o]
                            : double.NaN;
                        series.Add(v);
                        convergenceTree.Append(new GH_Number(v), path);
                    }
                    slopes[o] = ComputeConvergenceSlope(series);
                }

                // ── Info row ──
                double meanDisp = bestDispPerRerun.Where(double.IsFinite).DefaultIfEmpty(double.MaxValue).Average();
                double minDisp = bestDispPerRerun.Where(double.IsFinite).DefaultIfEmpty(double.MaxValue).Min();
                double maxDisp = bestDispPerRerun.Where(double.IsFinite).DefaultIfEmpty(double.MaxValue).Max();

                infoBuilder.AppendLine(string.Format(
                    "C{0,-3} | T[{1}] | S[{2}] | {3:F4} / {4:F4} / {5:F4} | obj0={6:F4} obj1={7:F4} obj2={8:F4}",
                    ci,
                    string.Join(",", topoIds),
                    string.Join(",", shapeIds),
                    meanDisp, minDisp, maxDisp,
                    slopes[0], slopes[1], slopes[2]));
            }

            infoBuilder.AppendLine();
            infoBuilder.AppendLine("Convergence slope = (last - first) / max(|first|, 1e-9). Negative = improving (minimization).");
            infoBuilder.AppendLine("Objective order: 0 = log(1+Disp%SLS), 1 = AvgUtil dev (or MaxUtil), 2 = RawFeas.");

            DA.SetData(0, infoBuilder.ToString());
            DA.SetDataTree(1, linesTree);
            DA.SetDataTree(2, labelsTree);
            DA.SetDataTree(3, convergenceTree);
            DA.SetDataTree(4, dispTree);
            DA.SetDataTree(5, anchorTree);
        }

        // ─── Combination resolution ────────────────────────────────────

        private List<(List<int> topo, List<int> shape)> ResolveCombinations(
            GH_Structure<GH_Integer> topoTree,
            GH_Structure<GH_Integer> shapeTree,
            List<int> topoCand,
            List<int> shapeCand,
            out string error)
        {
            error = null;
            var combos = new List<(List<int> topo, List<int> shape)>();

            bool hasTopoTree = topoTree != null && topoTree.PathCount > 0 && topoTree.DataCount > 0;
            bool hasShapeTree = shapeTree != null && shapeTree.PathCount > 0 && shapeTree.DataCount > 0;

            if (hasTopoTree && hasShapeTree)
            {
                int branches = Math.Min(topoTree.PathCount, shapeTree.PathCount);
                if (topoTree.PathCount != shapeTree.PathCount)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Format("Topo Combos has {0} branches but Shape Combos has {1}; using min = {2}.",
                            topoTree.PathCount, shapeTree.PathCount, branches));
                }
                for (int i = 0; i < branches; i++)
                {
                    var topo = topoTree.Branches[i].Where(g => g != null).Select(g => g.Value).Distinct().ToList();
                    var shape = shapeTree.Branches[i].Where(g => g != null).Select(g => g.Value).Distinct().ToList();
                    if (topo.Count > 0 && shape.Count > 0)
                        combos.Add((topo, shape));
                }
                if (combos.Count == 0)
                {
                    error = "Topo Combos / Shape Combos provided but all branches were empty.";
                }
                return combos;
            }

            if (topoCand.Count > 0 && shapeCand.Count > 0)
            {
                foreach (int t in topoCand.Distinct())
                    foreach (int s in shapeCand.Distinct())
                        combos.Add((new List<int> { t }, new List<int> { s }));
                return combos;
            }

            error = "Provide either (Topo Combos + Shape Combos) trees or (Topo Candidates + Shape Candidates) lists.";
            return combos;
        }

        // ─── GA loop (NSGA-II) ─────────────────────────────────────────

        private (List<GAIndividual> finalPop, List<SG_Shape> finalShapes, List<double[]> perGenBest)
            RunOneGA(SG_Shape iniShape, List<SG_Rule> rules, GrammarInterpreterSettings settings, FeasibilitySettings feas)
        {
            var moga = new SG_MOGA
            {
                PopulationSize = settings.PopulationSize,
                NumGenerations = settings.Generations,
                MutationProbability = settings.MutationProb,
                CrossoverProbability = settings.CrossoverProb,
                EliteProbability = settings.EliteProb,
                BlxAlpha = 0.3,
                NumObjectives = settings.NumObjectives
            };

            var clusterer = new SG_GA
            {
                PopulationSize = settings.PopulationSize,
                NumGenerations = settings.Generations,
                NumClusters = settings.Clusters,
                MutationProbability = settings.MutationProb,
                CrossoverProbability = settings.CrossoverProb,
                EliteProbability = settings.EliteProb,
                Maximize = false,
                InitialBoost = 6,
                BlxAlpha = 0.3,
                TopoWeight = settings.TopologyWeight,
                ShapeWeight = settings.ShapeWeight,
                FitnessWeight = settings.FitnessWeight,
                KMeansMaxIterations = settings.KMeansIterations,
                ReclusterInterval = settings.ReclusterInterval,
                MetricDomains = settings.MetricDomains != null && settings.MetricDomains.Count > 0
                    ? new List<Interval>(settings.MetricDomains) : null,
                ClusterEliteCount = settings.ClusterElite
            };

            var chromoLengths = StructuralEvaluator.GetChromosomeLengths(rules, iniShape);
            var ruleMarkers = rules.Select(r => r.RuleMarker).ToList();

            var population = moga.CreateInitialGeneration(settings.PopulationSize, chromoLengths, ruleMarkers);

            var perGenBest = new List<double[]>();
            List<GAIndividual> evaluatedPop = null;
            List<SG_Shape> evaluatedShapes = null;

            for (int g = 0; g < settings.Generations; g++)
            {
                var iniClone = iniShape.DeepCopy();
                var outcome = StructuralEvaluator.EvaluatePopulation(population, iniClone, rules, settings, feas);
                evaluatedPop = outcome.EvaluatedPopulation;
                evaluatedShapes = outcome.Shapes;

                // Per-generation min per objective (Pareto-front-agnostic; we want
                // the best achievable value per objective independent of trade-offs)
                var bestObjs = new double[settings.NumObjectives];
                for (int o = 0; o < settings.NumObjectives; o++)
                {
                    bestObjs[o] = evaluatedPop
                        .Where(ind => ind.ObjectiveValues != null
                                   && ind.ObjectiveValues.Count > o
                                   && double.IsFinite(ind.ObjectiveValues[o])
                                   && ind.ObjectiveValues[o] < double.MaxValue * 0.5)
                        .Select(ind => ind.ObjectiveValues[o])
                        .DefaultIfEmpty(double.NaN)
                        .Min();
                }
                perGenBest.Add(bestObjs);

                bool isLast = g >= settings.Generations - 1;
                if (!isLast)
                {
                    population = moga.ProcessEvaluatedIndividuals(evaluatedPop);
                    moga.IncrementGeneration();
                    clusterer.ClusterPopulation(evaluatedPop);
                }
                else
                {
                    moga.ProcessEvaluatedIndividuals(evaluatedPop);
                    clusterer.ClusterPopulation(evaluatedPop);
                }
            }

            return (evaluatedPop ?? new List<GAIndividual>(),
                    evaluatedShapes ?? new List<SG_Shape>(),
                    perGenBest);
        }

        // ─── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// First-vs-last normalized slope. Negative values mean the objective
        /// improved (we minimize). NaN if there is no finite first/last value.
        /// </summary>
        private static double ComputeConvergenceSlope(List<double> series)
        {
            if (series == null || series.Count < 2) return double.NaN;
            double first = series.FirstOrDefault(double.IsFinite);
            double last = series.LastOrDefault(double.IsFinite);
            if (!double.IsFinite(first) || !double.IsFinite(last)) return double.NaN;
            double denom = Math.Max(Math.Abs(first), 1e-9);
            return (last - first) / denom;
        }

        private static GrammarInterpreterSettings CloneSettings(GrammarInterpreterSettings src)
        {
            return new GrammarInterpreterSettings
            {
                PopulationSize = src.PopulationSize,
                Generations = src.Generations,
                Clusters = src.Clusters,
                MutationProb = src.MutationProb,
                CrossoverProb = src.CrossoverProb,
                EliteProb = src.EliteProb,
                TopologyWeight = src.TopologyWeight,
                ShapeWeight = src.ShapeWeight,
                FitnessWeight = src.FitnessWeight,
                KMeansIterations = src.KMeansIterations,
                ReclusterInterval = src.ReclusterInterval,
                TopologyMetrics = src.TopologyMetrics != null ? new List<int>(src.TopologyMetrics) : new List<int>(),
                ShapeMetrics = src.ShapeMetrics != null ? new List<int>(src.ShapeMetrics) : new List<int>(),
                ShapeShrinkWrapDetailRatio = src.ShapeShrinkWrapDetailRatio,
                FixedSeed = src.FixedSeed,
                DanglingWeight = src.DanglingWeight,
                AngleWeight = src.AngleWeight,
                LengthWeight = src.LengthWeight,
                IntersectionWeight = src.IntersectionWeight,
                RepetWeight = src.RepetWeight,
                DuplicateWeight = src.DuplicateWeight,
                AngleMinDeg = src.AngleMinDeg,
                AngleOptDeg = src.AngleOptDeg,
                LenTooShort = src.LenTooShort,
                LenOptLow = src.LenOptLow,
                LenOptHigh = src.LenOptHigh,
                LenTooLong = src.LenTooLong,
                NumObjectives = src.NumObjectives,
                SingleObjType = src.SingleObjType,
                UtilObjType = src.UtilObjType,
                SelfWeight = src.SelfWeight,
                CroSecOpt = src.CroSecOpt,
                MetricDomains = src.MetricDomains != null ? new List<Interval>(src.MetricDomains) : new List<Interval>(),
                GravityDir = src.GravityDir,
                ClusterElite = src.ClusterElite,
                CSOptIterations = src.CSOptIterations
            };
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("9C8D7E6F-1A2B-4C3D-8E9F-0A1B2C3D4E5F");
    }
}
