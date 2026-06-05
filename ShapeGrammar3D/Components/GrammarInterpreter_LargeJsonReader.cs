using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using Rhino.Geometry;
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
    /// Reads JSON files written by the large grammar interpreters (v2 schema) once and
    /// packs the parsed run into an <see cref="OptimizationResults"/> bundle for fast,
    /// repeated previewing (see GI_Opti Preview). Optionally rebuilds the selected
    /// individuals' models into a lightweight assembly.
    ///
    /// Outputs are kept to a minimum: heavy per-generation Pareto/convergence rendering
    /// now lives in the dedicated preview component, fed by the single Results output.
    /// </summary>
    public class GrammarInterpreter_LargeJsonReader : GH_Component
    {
        public GrammarInterpreter_LargeJsonReader()
          : base("GI Large JSON Reader", "GI_LargeJson",
              "Parses a GI_LargeSg JSON run once into an Optimization Results bundle (feed GI_Opti Preview). Optionally rebuilds selected individuals' models.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("JSON Path", "JSON", "Path to GI_LargeSg JSON file.", GH_ParamAccess.item);                               // 0
            pManager.AddParameter(new Param_LargeRunContext(), "Run Context", "RunCtx",
                "Bundle of SG_Shape seed + ordered rules emitted by the large interpreter. Required when Build Models = true so models can be rebuilt without re-wiring SG_Shape and Autorules.",
                GH_ParamAccess.item);                                                                                                          // 1
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all.", GH_ParamAccess.list, -1);                       // 2
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual index(es) within each generation. -1 = all.", GH_ParamAccess.list, -1); // 3
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Keep only top N individuals per (gen, cluster). 0 = no filter. Use to trim very large runs.", GH_ParamAccess.item, 0);     // 4
            pManager.AddBooleanParameter("Build Models", "Build", "If true and Run Context supplied, rebuild filtered models into the Assembly output.", GH_ParamAccess.item, false); // 5

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Run metadata, filter summary and rebuild status.", GH_ParamAccess.item);               // 0
            pManager.AddParameter(new Param_OptimizationResults(), "Results", "Res",
                "Pre-parsed run (aggregates + data points). Wire into GI_Opti Preview for Pareto/convergence with per-cluster sub-branches.",
                GH_ParamAccess.item);                                                                                                          // 1
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly",
                "Lightweight assembly with rebuilt SG_Shape + TB_Model for filtered individuals (only when Build Models = true).",
                GH_ParamAccess.item);                                                                                                          // 2
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string jsonPath = string.Empty;
            if (!DA.GetData(0, ref jsonPath)) return;

            GH_LargeRunContext ghContext = null;
            DA.GetData(1, ref ghContext);
            LargeRunContext runContext = ghContext?.Value;
            SG_Shape iniShape = runContext?.IniShape;
            var rules = runContext?.Rules != null ? new List<SG_Rule>(runContext.Rules) : new List<SG_Rule>();

            var genFilter = new List<int>();
            var indFilter = new List<int>();
            DA.GetDataList(2, genFilter);
            DA.GetDataList(3, indFilter);
            if (genFilter.Count == 0) genFilter.Add(-1);
            if (indFilter.Count == 0) indFilter.Add(-1);
            bool allGens = genFilter.Contains(-1);
            bool allInds = indFilter.Contains(-1);

            int topN = 0;
            bool buildModels = false;
            DA.GetData(4, ref topN);
            DA.GetData(5, ref buildModels);
            topN = Math.Max(0, topN);

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

                var results = new OptimizationResults
                {
                    JsonPath = jsonPath,
                    Version = version,
                    PopulationSize = ReadIntPath(root, "settings", "populationSize", 0),
                    NumGenerations = ReadIntPath(root, "settings", "generations", 0),
                    NumClusters = Math.Max(1, numClusters),
                    NumObjectives = Math.Max(1, numObjectives),
                    RunId = root.TryGetProperty("runId", out var rid) ? rid.GetString() : null
                };
                ReadMetricLabels(root, results);
                results.Aggregates = BuildAggregates(root);

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
                            rows.Add(ParseIndividual(i, idx, g));
                            idx++;
                        }

                        // Filter: ind index list, then top-N per cluster.
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

                        foreach (var r in filteredList.OrderBy(r => r.IndexInGen))
                        {
                            results.Points.Add(ToOptPoint(r, numObjectives));
                            if (buildModels) rebuildList.Add(new RebuildItem { Generation = g, Row = r });
                        }
                    }
                }

                // ── Optional rebuild ──
                string buildStatus;
                SGShapeGrammar3DAssembly assembly = new SGShapeGrammar3DAssembly();
                if (buildModels)
                {
                    if (iniShape == null || rules == null || rules.Count == 0)
                    {
                        buildStatus = "Build Models requested but Run Context (SG_Shape seed + rules) not supplied; rebuild skipped.";
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
                    buildStatus = results.Points.Count > 0
                        ? string.Format("{0} individuals parsed (toggle Build Models to materialise their models).", results.Points.Count)
                        : "No individuals selected.";
                }

                DA.SetData(0, BuildInfo(root, jsonPath, version, results.Points.Count, buildStatus));
                DA.SetData(1, new GH_OptimizationResults(results));
                DA.SetData(2, new GH_SGAssembly(assembly));
            }
        }

        /// <summary>Same invalid check as <see cref="GI_ParetoAssembly"/>.</summary>
        private static bool IsInvalidFitness(double f)
        {
            return double.IsNaN(f) || double.IsInfinity(f) || f >= double.MaxValue * 0.5;
        }

        private static OptPoint ToOptPoint(IndividualRow row, int numObjectives)
        {
            return new OptPoint
            {
                Generation = row.Generation,
                IndexInGen = row.IndexInGen,
                Cluster = row.Cluster,
                Rank = row.Rank,
                Id = row.Id,
                Fitness = row.Fitness,
                Feas = row.Feas,
                Objectives = row.Objectives ?? new List<double>(),
                Raw = BuildParetoRawPoint(row, numObjectives) ?? Point3d.Unset
            };
        }

        /// <summary>
        /// X matches GI_Pareto (Assembly): displacement ratio max_disp / (span/300).
        /// Multi-objective: <see cref="GAIndividual.Fitness"/> is that ratio.
        /// Single-objective: Obj[0] is always dispRatio in StructuralEvaluator.
        /// Y = Obj[1] (utilisation objective). Z = Obj[2] for 3 objectives, else feasibility.
        /// Returns null when the point is invalid (NaN/infinite/sentinel fitness).
        /// </summary>
        private static Point3d? BuildParetoRawPoint(IndividualRow row, int numObjectives)
        {
            if (numObjectives >= 2 && IsInvalidFitness(row.Fitness))
                return null;

            double x = GetDispRatioForPareto(row, numObjectives);
            if (double.IsNaN(x) || double.IsInfinity(x))
                return null;

            double y = row.Objectives != null && row.Objectives.Count > 1 ? row.Objectives[1] : double.NaN;
            if (double.IsNaN(y) || double.IsInfinity(y)) return null;

            double z = numObjectives >= 3 && row.Objectives != null && row.Objectives.Count > 2
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

        private static IndividualRow ParseIndividual(JsonElement e, int idxInGen, int gen)
        {
            return new IndividualRow
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
        }

        private static List<OptAggregate> BuildAggregates(JsonElement root)
        {
            var list = new List<OptAggregate>();
            if (!root.TryGetProperty("aggregates", out var aggEl) || aggEl.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var a in aggEl.EnumerateArray())
            {
                int g = a.TryGetProperty("g", out var gE) ? gE.GetInt32()
                      : a.TryGetProperty("Generation", out var gE2) ? gE2.GetInt32() : 0;
                int c = a.TryGetProperty("c", out var cE) ? cE.GetInt32()
                      : a.TryGetProperty("Cluster", out var cE2) ? cE2.GetInt32() : 0;
                int n = a.TryGetProperty("n", out var nE) ? nE.GetInt32()
                      : a.TryGetProperty("Count", out var nE2) ? nE2.GetInt32() : 0;

                list.Add(new OptAggregate
                {
                    Generation = g,
                    Cluster = c,
                    Count = n,
                    Best = ReadDouble(a, "best", "BestFitness"),
                    Worst = ReadDouble(a, "worst", "WorstFitness"),
                    Avg = ReadDouble(a, "avg", "AvgFitness")
                });
            }
            return list;
        }

        private static void ReadMetricLabels(JsonElement root, OptimizationResults results)
        {
            if (!root.TryGetProperty("metricLabels", out var ml) || ml.ValueKind != JsonValueKind.Object)
                return;
            if (ml.TryGetProperty("topology", out var tl) && tl.ValueKind == JsonValueKind.Array)
                results.TopoLabels = tl.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList();
            if (ml.TryGetProperty("shape", out var sl) && sl.ValueKind == JsonValueKind.Array)
                results.ShapeLabels = sl.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList();
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

        private static double Sortable(double fit)
            => double.IsNaN(fit) || double.IsInfinity(fit) ? double.MaxValue : fit;

        private static string BuildInfo(JsonElement root, string path, int version, int filteredCount, string buildStatus)
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
            sb.AppendLine("Points parsed into Results: " + filteredCount);
            sb.AppendLine(buildStatus);
            sb.AppendLine("Wire Results into GI_Opti Preview for Pareto (raw + normalized), per-cluster sub-branches, lines and labels.");
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
