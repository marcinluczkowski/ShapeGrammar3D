using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Reads JSON files written by <see cref="GrammarInterpreter_LargeFromSgShape"/>
    /// (v2 schema). Outputs convergence aggregates, per-generation Pareto data and -
    /// when a SG_Shape and rules are supplied - a lightweight assembly with the
    /// requested individuals' models rebuilt by replaying their genotypes.
    ///
    /// The reader also tolerates v1 files (aggregates only) so legacy summaries
    /// keep producing convergence graphs.
    /// </summary>
    public class GrammarInterpreter_LargeJsonReader : GH_Component
    {
        public GrammarInterpreter_LargeJsonReader()
          : base("GI Large JSON Reader", "GI_LargeJson",
              "Reads GI_LargeSg JSON runs: convergence, Pareto, and on-demand model rebuilds for selected (generation, individual) pairs.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("JSON Path", "JSON", "Path to GI_LargeSg JSON file.", GH_ParamAccess.item);                               // 0
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "Initial SG_Shape used during the original run (required for model rebuild).", GH_ParamAccess.item); // 1
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Same rule list as the original run (required for model rebuild).", GH_ParamAccess.list); // 2
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all.", GH_ParamAccess.list, -1);                       // 3
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual index(es) within each generation. -1 = all.", GH_ParamAccess.list, -1); // 4
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Keep only top N individuals per (gen, cluster). 0 = no filter.", GH_ParamAccess.item, 0);     // 5
            pManager.AddBooleanParameter("Build Models", "Build", "If true and SG_Shape+rules supplied, rebuild filtered models into the Assembly output.", GH_ParamAccess.item, false); // 6
            pManager.AddPointParameter("Insert Pt", "Pt", "Origin for the convergence graph.", GH_ParamAccess.item, Point3d.Origin);            // 7
            pManager.AddNumberParameter("Graph W", "dX", "Convergence graph width.", GH_ParamAccess.item, 15.0);                                // 8
            pManager.AddNumberParameter("Graph H", "dY", "Convergence graph height.", GH_ParamAccess.item, 8.0);                                // 9

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Run metadata + filter summary.", GH_ParamAccess.item);                                  // 0
            pManager.AddNumberParameter("Best Fitness", "Best", "Best fitness per {gen;cluster}.", GH_ParamAccess.tree);                       // 1
            pManager.AddNumberParameter("Worst Fitness", "Worst", "Worst fitness per {gen;cluster}.", GH_ParamAccess.tree);                    // 2
            pManager.AddNumberParameter("Average Fitness", "Avg", "Average fitness per {gen;cluster}.", GH_ParamAccess.tree);                  // 3
            pManager.AddIntegerParameter("Counts", "N", "Individual count per {gen;cluster}.", GH_ParamAccess.tree);                           // 4
            pManager.AddLineParameter("Conv Lines", "CLn", "Best-fitness convergence lines.", GH_ParamAccess.tree);                            // 5
            pManager.AddColourParameter("Conv Colours", "CCol", "Convergence line colours.", GH_ParamAccess.tree);                             // 6
            pManager.AddPointParameter("Pareto Raw", "PtoRaw",
                "Same axes as GI_Pareto (Assembly): X = SLS disp ratio (max disp / span×300), i.e. multi-obj Fitness or single-obj Obj[0]; Y = utilisation objective Obj[1] (deviation or max util per run settings); Z = Obj[2] if 3 objectives else Feas.",
                GH_ParamAccess.tree);                                                                                                              // 7
            pManager.AddPointParameter("Pareto Normalized", "PtoNrm",
                "Per-generation points in [0,1]³: min-max normalize each axis with 5% padding (same idea as GI_Pareto axis mapping). Multiply by your graph W,H,D to overlay the assembly Pareto component.",
                GH_ParamAccess.tree);                                                                                                              // 8
            pManager.AddColourParameter("Pareto Colours", "PCol", "Cluster colour per Pareto point (matches Raw + Normalized).", GH_ParamAccess.tree);   // 9
            pManager.AddIntegerParameter("Filt Cluster", "FCls", "Cluster index of filtered individuals per {gen}.", GH_ParamAccess.tree);     // 10
            pManager.AddNumberParameter("Filt Fitness", "FFit", "Fitness of filtered individuals per {gen}.", GH_ParamAccess.tree);            // 11
            pManager.AddTextParameter("Filt Id", "FId", "Stored id of filtered individuals per {gen}.", GH_ParamAccess.tree);                  // 12
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "Lightweight assembly with rebuilt SG_Shape + TB_Model for filtered individuals (only when Build Models = true).", GH_ParamAccess.item); // 13
            pManager.AddTextParameter("Build Status", "Status", "Notes on rebuild (warnings, mismatches).", GH_ParamAccess.item);              // 14
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string jsonPath = string.Empty;
            if (!DA.GetData(0, ref jsonPath)) return;

            SG_Shape iniShape = null;
            DA.GetData(1, ref iniShape);

            var rules = new List<SG_Rule>();
            DA.GetDataList(2, rules);

            var genFilter = new List<int>();
            var indFilter = new List<int>();
            DA.GetDataList(3, genFilter);
            DA.GetDataList(4, indFilter);
            if (genFilter.Count == 0) genFilter.Add(-1);
            if (indFilter.Count == 0) indFilter.Add(-1);
            bool allGens = genFilter.Contains(-1);
            bool allInds = indFilter.Contains(-1);

            int topN = 0;
            bool buildModels = false;
            Point3d insertPt = Point3d.Origin;
            double graphW = 15.0, graphH = 8.0;
            DA.GetData(5, ref topN);
            DA.GetData(6, ref buildModels);
            DA.GetData(7, ref insertPt);
            DA.GetData(8, ref graphW);
            DA.GetData(9, ref graphH);
            topN = Math.Max(0, topN);
            graphW = Math.Max(1.0, graphW);
            graphH = Math.Max(1.0, graphH);

            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "JSON file not found.");
                return;
            }

            JsonDocument doc;
            try
            {
                using var fs = File.OpenRead(jsonPath);
                doc = JsonDocument.Parse(fs, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to read JSON: " + ex.Message);
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;
                int version = root.TryGetProperty("version", out var verEl) && verEl.TryGetInt32(out int v) ? v : 1;

                int numClusters = ReadIntPath(root, "settings", "clusters", fallback: 1);
                int numObjectives = ReadIntPath(root, "settings", "numObjectives", fallback: 1);

                // ── Aggregates → trees + convergence graph ──
                var (best, worst, avg, count) = BuildAggregateTrees(root);
                var (lines, lineCols) = BuildConvergenceGraph(root, numClusters, insertPt, graphW, graphH);

                DA.SetDataTree(1, best);
                DA.SetDataTree(2, worst);
                DA.SetDataTree(3, avg);
                DA.SetDataTree(4, count);
                DA.SetDataTree(5, lines);
                DA.SetDataTree(6, lineCols);

                // ── Per-generation Pareto + filtered individuals ──
                var pareto = new GH_Structure<GH_Point>();
                var paretoNorm = new GH_Structure<GH_Point>();
                var paretoColours = new GH_Structure<GH_Colour>();
                var filtClust = new GH_Structure<GH_Integer>();
                var filtFit = new GH_Structure<GH_Number>();
                var filtId = new GH_Structure<GH_String>();

                var rebuildList = new List<RebuildItem>();

                if (version >= 2 && root.TryGetProperty("generations", out var gensEl) && gensEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var genEl in gensEl.EnumerateArray())
                    {
                        int g = genEl.TryGetProperty("g", out var gE) ? gE.GetInt32() : -1;
                        if (!allGens && !genFilter.Contains(g)) continue;
                        if (!genEl.TryGetProperty("ind", out var indEl) || indEl.ValueKind != JsonValueKind.Array) continue;

                        var rows = new List<IndividualRow>();
                        int idx = 0;
                        foreach (var i in indEl.EnumerateArray())
                        {
                            var row = ParseIndividual(i, idx, g);
                            rows.Add(row);

                            if (row.Objectives != null && row.Objectives.Count >= 2)
                            {
                                var raw = BuildParetoRawPoint(row, numObjectives);
                                if (raw.HasValue)
                                {
                                    var path = new GH_Path(g);
                                    pareto.Append(new GH_Point(raw.Value), path);
                                    paretoColours.Append(new GH_Colour(GetClusterColour(row.Cluster, numClusters)), path);
                                }
                            }
                            idx++;
                        }

                        // Filter: ind index list, then top-N per cluster
                        IEnumerable<IndividualRow> filtered = rows;
                        if (!allInds) filtered = filtered.Where(r => indFilter.Contains(r.IndexInGen));
                        var filteredList = filtered.ToList();
                        if (topN > 0)
                        {
                            filteredList = filteredList
                                .GroupBy(r => r.Cluster)
                                .SelectMany(grp => grp.OrderBy(r => Sortable(r.Fitness)).Take(topN))
                                .ToList();
                        }

                        var filtPath = new GH_Path(g);
                        foreach (var r in filteredList.OrderBy(r => r.IndexInGen))
                        {
                            filtClust.Append(new GH_Integer(r.Cluster), filtPath);
                            filtFit.Append(new GH_Number(Safe(r.Fitness)), filtPath);
                            filtId.Append(new GH_String(r.Id ?? string.Empty), filtPath);
                            if (buildModels) rebuildList.Add(new RebuildItem { Generation = g, Row = r });
                        }
                    }
                }

                if (pareto.DataCount > 0)
                    paretoNorm = NormalizeParetoPerBranch(pareto);

                DA.SetDataTree(7, pareto);
                DA.SetDataTree(8, paretoNorm);
                DA.SetDataTree(9, paretoColours);
                DA.SetDataTree(10, filtClust);
                DA.SetDataTree(11, filtFit);
                DA.SetDataTree(12, filtId);

                // ── Optional rebuild ──
                string buildStatus;
                SGShapeGrammar3DAssembly assembly = new SGShapeGrammar3DAssembly();
                if (buildModels)
                {
                    if (iniShape == null || rules == null || rules.Count == 0)
                    {
                        buildStatus = "Build Models requested but SG_Shape and/or rules not supplied; rebuild skipped.";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, buildStatus);
                    }
                    else
                    {
                        try
                        {
                            assembly = RebuildAssembly(root, rebuildList, iniShape, rules, out buildStatus);
                        }
                        catch (Exception ex)
                        {
                            buildStatus = "Rebuild failed: " + ex.Message;
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, buildStatus);
                        }
                    }
                }
                else
                {
                    buildStatus = rebuildList.Count > 0
                        ? string.Format("{0} individuals filtered (toggle Build Models to materialise their models).", rebuildList.Count)
                        : "No individuals selected.";
                }

                DA.SetData(13, new GH_SGAssembly(assembly));
                DA.SetData(14, buildStatus);

                // ── Info text ──
                DA.SetData(0, BuildInfo(root, jsonPath, version, rebuildList.Count));
            }
        }

        /// <summary>Same invalid check as <see cref="GI_ParetoAssembly"/>.</summary>
        private static bool IsInvalidFitness(double f)
        {
            return double.IsNaN(f) || double.IsInfinity(f) || f >= double.MaxValue * 0.5;
        }

        /// <summary>
        /// X matches GI_Pareto (Assembly): displacement ratio max_disp / (span/300).
        /// Multi-objective: <see cref="GAIndividual.Fitness"/> is that ratio.
        /// Single-objective: Fitness may be a composite; Obj[0] is always dispRatio in StructuralEvaluator.
        /// Y = Obj[1] (utilisation objective: deviation from target or max util — see Json utilObjType).
        /// Z = Obj[2] for 3 objectives, else total feasibility violation from field <c>fe</c>.
        /// </summary>
        private static Point3d? BuildParetoRawPoint(IndividualRow row, int numObjectives)
        {
            if (numObjectives >= 2 && IsInvalidFitness(row.Fitness))
                return null;

            double x = GetDispRatioForPareto(row, numObjectives);
            if (double.IsNaN(x) || double.IsInfinity(x))
                return null;

            double y = row.Objectives[1];
            if (double.IsNaN(y) || double.IsInfinity(y)) return null;

            double z = numObjectives >= 3 && row.Objectives.Count > 2
                ? row.Objectives[2]
                : row.Feas;
            if (double.IsNaN(z) || double.IsInfinity(z)) z = 0;

            return new Point3d(x, y, z);
        }

        private static double GetDispRatioForPareto(IndividualRow row, int numObjectives)
        {
            if (numObjectives >= 2)
                return row.Fitness;
            return row.Objectives != null && row.Objectives.Count > 0 ? row.Objectives[0] : row.Fitness;
        }

        /// <summary>Per-path min–max normalise with 5% padding (same as GI_Pareto Assembly).</summary>
        private static GH_Structure<GH_Point> NormalizeParetoPerBranch(GH_Structure<GH_Point> raw)
        {
            var norm = new GH_Structure<GH_Point>();
            if (raw == null || raw.PathCount == 0) return norm;

            for (int bi = 0; bi < raw.PathCount; bi++)
            {
                GH_Path path = raw.get_Path(bi);
                var branch = raw.get_Branch(path);
                var pts = new List<Point3d>();
                foreach (var goo in branch)
                {
                    if (goo is GH_Point gp && gp.Value != null) pts.Add(gp.Value);
                }
                if (pts.Count == 0) continue;

                double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                double minZ = pts.Min(p => p.Z), maxZ = pts.Max(p => p.Z);
                PadRange(ref minX, ref maxX);
                PadRange(ref minY, ref maxY);
                PadRange(ref minZ, ref maxZ);
                double rX = maxX - minX, rY = maxY - minY, rZ = maxZ - minZ;
                if (rX < 1e-18) rX = 1;
                if (rY < 1e-18) rY = 1;
                if (rZ < 1e-18) rZ = 1;

                foreach (var goo in branch)
                {
                    if (goo is GH_Point gp && gp.Value != null)
                    {
                        var p = gp.Value;
                        double nx = Math.Clamp((p.X - minX) / rX, 0, 1);
                        double ny = Math.Clamp((p.Y - minY) / rY, 0, 1);
                        double nz = Math.Clamp((p.Z - minZ) / rZ, 0, 1);
                        norm.Append(new GH_Point(new Point3d(nx, ny, nz)), path);
                    }
                }
            }
            return norm;
        }

        private static void PadRange(ref double min, ref double max)
        {
            if (max <= min) max = min + 1;
            double r = max - min;
            min -= r * 0.05;
            max += r * 0.05;
        }

        private static IndividualRow ParseIndividual(JsonElement e, int idxInGen, int gen)
        {
            var row = new IndividualRow
            {
                Generation = gen,
                IndexInGen = idxInGen,
                Id = e.TryGetProperty("id", out var idE) ? idE.GetString() : null,
                Cluster = e.TryGetProperty("c", out var cE) && cE.TryGetInt32(out var c) ? c : -1,
                Fitness = ReadDouble(e, "f"),
                Rank = e.TryGetProperty("rk", out var rkE) && rkE.TryGetInt32(out var rk) ? rk : -1,
                CrowdingDistance = ReadDouble(e, "cd"),
                Feas = ReadDouble(e, "fe"),
                VDang = ReadDouble(e, "vd"),
                VAng = ReadDouble(e, "va"),
                VLen = ReadDouble(e, "vl"),
                Objectives = ReadDoubleArray(e, "o"),
                Topo = ReadDoubleArray(e, "t"),
                Shape = ReadDoubleArray(e, "s"),
                Chromosome = ReadIntArray(e, "ch"),
                ChromosomeParam = ReadDoubleArray(e, "p")
            };
            return row;
        }

        private static (GH_Structure<GH_Number> best, GH_Structure<GH_Number> worst,
                        GH_Structure<GH_Number> avg, GH_Structure<GH_Integer> count)
            BuildAggregateTrees(JsonElement root)
        {
            var best = new GH_Structure<GH_Number>();
            var worst = new GH_Structure<GH_Number>();
            var avg = new GH_Structure<GH_Number>();
            var count = new GH_Structure<GH_Integer>();

            if (!root.TryGetProperty("aggregates", out var aggEl) || aggEl.ValueKind != JsonValueKind.Array)
                return (best, worst, avg, count);

            foreach (var a in aggEl.EnumerateArray())
            {
                int g = a.TryGetProperty("g", out var gE) ? gE.GetInt32()
                      : a.TryGetProperty("Generation", out var gE2) ? gE2.GetInt32() : 0;
                int c = a.TryGetProperty("c", out var cE) ? cE.GetInt32()
                      : a.TryGetProperty("Cluster", out var cE2) ? cE2.GetInt32() : 0;
                int n = a.TryGetProperty("n", out var nE) ? nE.GetInt32()
                      : a.TryGetProperty("Count", out var nE2) ? nE2.GetInt32() : 0;

                double bestVal = ReadDouble(a, "best", "BestFitness");
                double worstVal = ReadDouble(a, "worst", "WorstFitness");
                double avgVal = ReadDouble(a, "avg", "AvgFitness");

                var path = new GH_Path(g, c);
                best.Append(new GH_Number(bestVal), path);
                worst.Append(new GH_Number(worstVal), path);
                avg.Append(new GH_Number(avgVal), path);
                count.Append(new GH_Integer(n), path);
            }

            return (best, worst, avg, count);
        }

        private static (GH_Structure<GH_Line> lines, GH_Structure<GH_Colour> cols)
            BuildConvergenceGraph(JsonElement root, int numClusters, Point3d origin, double w, double h)
        {
            var lines = new GH_Structure<GH_Line>();
            var cols = new GH_Structure<GH_Colour>();
            if (!root.TryGetProperty("aggregates", out var aggEl) || aggEl.ValueKind != JsonValueKind.Array)
                return (lines, cols);

            var byCluster = new Dictionary<int, List<(int g, double best)>>();
            foreach (var a in aggEl.EnumerateArray())
            {
                int g = a.TryGetProperty("g", out var gE) ? gE.GetInt32()
                      : a.TryGetProperty("Generation", out var gE2) ? gE2.GetInt32() : 0;
                int c = a.TryGetProperty("c", out var cE) ? cE.GetInt32()
                      : a.TryGetProperty("Cluster", out var cE2) ? cE2.GetInt32() : 0;
                double bestVal = ReadDouble(a, "best", "BestFitness");
                if (!byCluster.TryGetValue(c, out var list))
                {
                    list = new List<(int, double)>();
                    byCluster[c] = list;
                }
                list.Add((g, bestVal));
            }
            if (byCluster.Count == 0) return (lines, cols);

            var allGens = byCluster.Values.SelectMany(v => v.Select(t => t.g)).Distinct().OrderBy(x => x).ToList();
            if (allGens.Count < 2) return (lines, cols);

            double subH = h / Math.Max(1, numClusters);
            for (int c = 0; c < Math.Max(1, numClusters); c++)
            {
                if (!byCluster.TryGetValue(c, out var data) || data.Count < 2) continue;
                data = data.OrderBy(t => t.g).ToList();
                var valid = data.Where(t => !double.IsNaN(t.best) && !double.IsInfinity(t.best)).ToList();
                if (valid.Count == 0) continue;
                double min = valid.Min(t => t.best);
                double max = valid.Max(t => t.best);
                if (Math.Abs(max - min) < 1e-12) max = min + 1.0;

                double yBase = origin.Y - c * subH;
                var path = new GH_Path(c);
                for (int i = 1; i < data.Count; i++)
                {
                    Point3d p0 = MapPoint(data[i - 1].g, data[i - 1].best, allGens.Count, min, max, origin.X, yBase, w, subH * 0.8);
                    Point3d p1 = MapPoint(data[i].g, data[i].best, allGens.Count, min, max, origin.X, yBase, w, subH * 0.8);
                    lines.Append(new GH_Line(new Line(p0, p1)), path);
                    cols.Append(new GH_Colour(GetClusterColour(c, numClusters)), path);
                }
            }
            return (lines, cols);
        }

        private static Point3d MapPoint(int gen, double best, int genCount, double min, double max,
            double x0, double y0, double w, double h)
        {
            double x = x0 + (genCount <= 1 ? 0.0 : (double)gen / (genCount - 1) * w);
            double t = (best - min) / (max - min);
            double y = y0 + (1.0 - t) * h;
            return new Point3d(x, y, 0);
        }

        private static SGShapeGrammar3DAssembly RebuildAssembly(JsonElement root,
            List<RebuildItem> items, SG_Shape iniShape, List<SG_Rule> rules, out string status)
        {
            var settings = ReadSettings(root);
            var feas = ReadFeasibility(root);
            int populationSize = items.Count;
            int generations = ReadIntPath(root, "settings", "generations", 0);
            int numClusters = ReadIntPath(root, "settings", "clusters", 1);
            int numObjectives = ReadIntPath(root, "settings", "numObjectives", 1);

            var orderedRules = StructuralEvaluator.EnsureInitShapeFirst(rules);

            var assembly = new SGShapeGrammar3DAssembly
            {
                Config = new AssemblyConfig
                {
                    PopulationSize = populationSize,
                    NumGenerations = generations,
                    NumClusters = numClusters,
                    NumObjectives = numObjectives,
                    TopoMetricTypes = settings.TopologyMetrics != null ? new List<int>(settings.TopologyMetrics) : new List<int>(),
                    ShapeMetricTypes = settings.ShapeMetrics != null ? new List<int>(settings.ShapeMetrics) : new List<int>()
                }
            };
            if (settings.TopologyMetrics != null)
                foreach (int t in settings.TopologyMetrics) assembly.MetricNames.Add("T:" + TopologyMetrics.GetLabel(t));
            if (settings.ShapeMetrics != null)
                foreach (int m in settings.ShapeMetrics) assembly.MetricNames.Add("S:" + ShapeMetrics.GetLabel(m));

            int ok = 0, fail = 0;
            foreach (var grp in items.GroupBy(it => it.Generation).OrderBy(g => g.Key))
            {
                var ag = new AssemblyGeneration { Generation = grp.Key };
                foreach (var item in grp)
                {
                    var row = item.Row;
                    if (row.Chromosome == null || row.ChromosomeParam == null)
                    {
                        fail++;
                        continue;
                    }

                    var ind = new GAIndividual(row.Chromosome, row.ChromosomeParam, row.Id ?? Guid.NewGuid().ToString("N").Substring(0, 8))
                    {
                        ClustGrp = row.Cluster,
                        Rank = row.Rank,
                        CrowdingDistance = double.IsNaN(row.CrowdingDistance) ? 0.0 : row.CrowdingDistance,
                        Feas = double.IsNaN(row.Feas) ? 0.0 : row.Feas,
                        VDang = double.IsNaN(row.VDang) ? 0.0 : row.VDang,
                        VAng = double.IsNaN(row.VAng) ? 0.0 : row.VAng,
                        VLen = double.IsNaN(row.VLen) ? 0.0 : row.VLen,
                        ObjectiveValues = row.Objectives ?? new List<double>(),
                        TopoValues = row.Topo ?? new List<double>(),
                        ShpeValues = row.Shape ?? new List<double>(),
                        Fitness = double.IsNaN(row.Fitness) ? double.MaxValue : row.Fitness
                    };

                    try
                    {
                        var pop = new List<GAIndividual> { ind };
                        var outcome = StructuralEvaluator.EvaluatePopulation(
                            pop, iniShape.DeepCopy(), orderedRules, settings, feas, deepCopyOutputs: false);
                        var shape = outcome.Shapes != null && outcome.Shapes.Count > 0 ? outcome.Shapes[0] : null;
                        var model = outcome.Models != null && outcome.Models.Count > 0 ? outcome.Models[0] : null;
                        ag.Individuals.Add(AssemblyIndividual.FromGAIndividual(ind, model, shape));
                        ok++;
                    }
                    catch
                    {
                        fail++;
                    }
                }
                if (ag.Individuals.Count > 0) assembly.Generations.Add(ag);
            }

            status = string.Format("Rebuilt {0} individuals across {1} generations ({2} failures).",
                ok, assembly.Generations.Count, fail);
            return assembly;
        }

        // ── JSON helpers ──

        private static double ReadDouble(JsonElement parent, params string[] candidates)
        {
            foreach (var name in candidates)
            {
                if (parent.TryGetProperty(name, out var el))
                {
                    if (el.ValueKind == JsonValueKind.Null) return double.NaN;
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double d)) return d;
                }
            }
            return double.NaN;
        }

        private static List<double> ReadDoubleArray(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return new List<double>();
            var list = new List<double>(el.GetArrayLength());
            foreach (var v in el.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.Null) list.Add(double.NaN);
                else if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)) list.Add(d);
                else list.Add(double.NaN);
            }
            return list;
        }

        private static List<int> ReadIntArray(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return new List<int>();
            var list = new List<int>(el.GetArrayLength());
            foreach (var v in el.EnumerateArray())
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) list.Add(i);
            return list;
        }

        private static int ReadIntPath(JsonElement root, string parent, string name, int fallback)
        {
            if (root.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object
                && p.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                && el.TryGetInt32(out int v))
                return v;
            // legacy v1 may have flat properties
            if (root.TryGetProperty(name, out var el2) && el2.ValueKind == JsonValueKind.Number && el2.TryGetInt32(out int v2))
                return v2;
            return fallback;
        }

        private static GrammarInterpreterSettings ReadSettings(JsonElement root)
        {
            var s = new GrammarInterpreterSettings();
            if (!root.TryGetProperty("settings", out var p) || p.ValueKind != JsonValueKind.Object)
                return s.Sanitize();

            s.PopulationSize = TryInt(p, "populationSize", s.PopulationSize);
            s.Generations = TryInt(p, "generations", s.Generations);
            s.Clusters = TryInt(p, "clusters", s.Clusters);
            s.NumObjectives = TryInt(p, "numObjectives", s.NumObjectives);
            s.SingleObjType = TryInt(p, "singleObjType", s.SingleObjType);
            s.UtilObjType = TryInt(p, "utilObjType", s.UtilObjType);
            s.MutationProb = TryDouble(p, "mutationProb", s.MutationProb);
            s.CrossoverProb = TryDouble(p, "crossoverProb", s.CrossoverProb);
            s.EliteProb = TryDouble(p, "eliteProb", s.EliteProb);
            s.TopologyWeight = TryDouble(p, "topologyWeight", s.TopologyWeight);
            s.ShapeWeight = TryDouble(p, "shapeWeight", s.ShapeWeight);
            s.FitnessWeight = TryDouble(p, "fitnessWeight", s.FitnessWeight);
            s.KMeansIterations = TryInt(p, "kMeansIterations", s.KMeansIterations);
            s.ReclusterInterval = TryInt(p, "reclusterInterval", s.ReclusterInterval);
            s.ClusterElite = TryInt(p, "clusterElite", s.ClusterElite);
            s.CSOptIterations = TryInt(p, "csOptIterations", s.CSOptIterations);
            s.CroSecOpt = TryInt(p, "croSecOpt", s.CroSecOpt);
            s.SelfWeight = TryBool(p, "useSelfWeight", s.SelfWeight);
            s.FixedSeed = TryBool(p, "fixedSeed", s.FixedSeed);
            s.ShapeShrinkWrapDetailRatio = TryDouble(p, "shapeShrinkWrapDetailRatio", s.ShapeShrinkWrapDetailRatio);

            if (p.TryGetProperty("topologyMetrics", out var tm) && tm.ValueKind == JsonValueKind.Array)
            {
                s.TopologyMetrics = new List<int>(tm.GetArrayLength());
                foreach (var v in tm.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) s.TopologyMetrics.Add(i);
            }
            if (p.TryGetProperty("shapeMetrics", out var sm) && sm.ValueKind == JsonValueKind.Array)
            {
                s.ShapeMetrics = new List<int>(sm.GetArrayLength());
                foreach (var v in sm.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) s.ShapeMetrics.Add(i);
            }
            if (p.TryGetProperty("metricDomains", out var md) && md.ValueKind == JsonValueKind.Array)
            {
                s.MetricDomains = new List<Interval>(md.GetArrayLength());
                foreach (var v in md.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.Object) continue;
                    double mn = TryDouble(v, "min", 0);
                    double mx = TryDouble(v, "max", 1);
                    s.MetricDomains.Add(new Interval(mn, mx));
                }
            }
            if (p.TryGetProperty("gravity", out var g) && g.ValueKind == JsonValueKind.Array && g.GetArrayLength() == 3)
            {
                var arr = g.EnumerateArray().ToArray();
                s.GravityDir = new Vector3d(
                    arr[0].TryGetDouble(out var gx) ? gx : 0,
                    arr[1].TryGetDouble(out var gy) ? gy : 0,
                    arr[2].TryGetDouble(out var gz) ? gz : -1);
            }

            return s.Sanitize();
        }

        private static FeasibilitySettings ReadFeasibility(JsonElement root)
        {
            var f = FeasibilitySettings.Default();
            if (!root.TryGetProperty("settings", out var settings)
                || !settings.TryGetProperty("feasibility", out var p)
                || p.ValueKind != JsonValueKind.Object)
                return f;

            f.WDang = TryDouble(p, "danglingWeight", f.WDang);
            f.WAng = TryDouble(p, "angleWeight", f.WAng);
            f.WLen = TryDouble(p, "lengthWeight", f.WLen);
            f.WIntersect = TryDouble(p, "intersectionWeight", f.WIntersect);
            f.WRepet = TryDouble(p, "repetWeight", f.WRepet);
            f.WDup = TryDouble(p, "duplicateWeight", f.WDup);
            f.AngleMinDeg = TryDouble(p, "angleMinDeg", f.AngleMinDeg);
            f.AngleOptDeg = TryDouble(p, "angleOptDeg", f.AngleOptDeg);
            f.LenTooShort = TryDouble(p, "lenTooShort", f.LenTooShort);
            f.LenOptLow = TryDouble(p, "lenOptLow", f.LenOptLow);
            f.LenOptHigh = TryDouble(p, "lenOptHigh", f.LenOptHigh);
            f.LenTooLong = TryDouble(p, "lenTooLong", f.LenTooLong);
            return f;
        }

        private static int TryInt(JsonElement p, string name, int fallback)
            => p.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : fallback;

        private static double TryDouble(JsonElement p, string name, double fallback)
            => p.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v) ? v : fallback;

        private static bool TryBool(JsonElement p, string name, bool fallback)
            => p.TryGetProperty(name, out var el) && (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
                ? el.GetBoolean() : fallback;

        private static double Safe(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;

        private static double Sortable(double fit)
            => double.IsNaN(fit) || double.IsInfinity(fit) ? double.MaxValue : fit;

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0.0, 1.0);
            int g = t <= 0.5 ? (int)(t / 0.5 * 255) : 255;
            int b = t <= 0.5 ? 255 : (int)((1.0 - (t - 0.5) / 0.5) * 255);
            return Color.FromArgb(0, Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        private static string BuildInfo(JsonElement root, string path, int version, int filteredCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("GI_LargeSg JSON summary");
            sb.AppendLine("Path: " + path);
            sb.AppendLine("Version: " + version);
            if (root.TryGetProperty("runId", out var rid)) sb.AppendLine("RunId: " + rid.GetString());
            if (root.TryGetProperty("startedAt", out var sa)) sb.AppendLine("Started: " + sa.GetString());
            if (root.TryGetProperty("finishedAt", out var fa)) sb.AppendLine("Finished: " + fa.GetString());
            sb.AppendLine(string.Format("Population: {0}, Generations: {1}, Clusters: {2}, Objectives: {3}",
                ReadIntPath(root, "settings", "populationSize", 0),
                ReadIntPath(root, "settings", "generations", 0),
                ReadIntPath(root, "settings", "clusters", 0),
                ReadIntPath(root, "settings", "numObjectives", 0)));
            if (root.TryGetProperty("totalIndividualsRecorded", out var tot) && tot.ValueKind == JsonValueKind.Number)
                sb.AppendLine("Individuals recorded: " + tot.GetInt64());
            if (root.TryGetProperty("rules", out var rls) && rls.ValueKind == JsonValueKind.Array)
                sb.AppendLine("Rules: " + string.Join(", ",
                    rls.EnumerateArray().Select(r => r.TryGetProperty("type", out var t) ? t.GetString() : "?")));
            if (root.TryGetProperty("metricLabels", out var ml) && ml.ValueKind == JsonValueKind.Object)
            {
                if (ml.TryGetProperty("topology", out var tl) && tl.ValueKind == JsonValueKind.Array)
                    sb.AppendLine("Topo: " + string.Join(", ", tl.EnumerateArray().Select(s => s.GetString())));
                if (ml.TryGetProperty("shape", out var sl) && sl.ValueKind == JsonValueKind.Array)
                    sb.AppendLine("Shape: " + string.Join(", ", sl.EnumerateArray().Select(s => s.GetString())));
            }
            sb.AppendLine("Pareto Raw: X=disp/(span/300), Y=utilisation objective, Z=feas (see component output descriptions).");
            sb.AppendLine("Pareto Normalized: same points scaled per-generation to [0,1]³ for plotting.");
            return sb.ToString();
        }

        // ── DTOs ──

        private class IndividualRow
        {
            public int Generation { get; set; }
            public int IndexInGen { get; set; }
            public string Id { get; set; }
            public int Cluster { get; set; }
            public double Fitness { get; set; }
            public int Rank { get; set; }
            public double CrowdingDistance { get; set; }
            public double Feas { get; set; }
            public double VDang { get; set; }
            public double VAng { get; set; }
            public double VLen { get; set; }
            public List<double> Objectives { get; set; }
            public List<double> Topo { get; set; }
            public List<double> Shape { get; set; }
            public List<int> Chromosome { get; set; }
            public List<double> ChromosomeParam { get; set; }
        }

        private class RebuildItem
        {
            public int Generation { get; set; }
            public IndividualRow Row { get; set; }
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;
        public override Guid ComponentGuid => new Guid("AE5D2B7C-67FA-40D4-B95A-1686A5505851");
    }
}
