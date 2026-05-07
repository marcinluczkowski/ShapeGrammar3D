using Grasshopper.Kernel;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Large-run interpreter seeded from a supplied SG_Shape. The component
    /// runs the GA, streams every individual into a single JSON v2 file
    /// (settings + per-individual genotype/objectives/cluster + per-cluster
    /// aggregates) and exposes only that file path. Designed for runs of
    /// hundreds of thousands of individuals where keeping models in memory
    /// would crash Grasshopper.
    /// </summary>
    public class GrammarInterpreter_LargeFromSgShape : GH_Component
    {
        private const bool MAXIMIZE = false;

        public GrammarInterpreter_LargeFromSgShape()
          : base("Grammar Interpreter Large from SG_Shape", "GI_LargeSg",
              "Large-scale GA from an SG_Shape input. Streams the full run to a single JSON file (settings, genotypes, objectives, cluster info, per-cluster aggregates). Use GI_LargeJson Reader to inspect.",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "Initial SG_Shape.", GH_ParamAccess.item);                          // 0
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for automatic interpreter.", GH_ParamAccess.list); // 1
            pManager.AddBooleanParameter("Reset", "Reset", "Reset & run the large analysis.", GH_ParamAccess.item, false);          // 2
            pManager.AddParameter(new Param_GrammarInterpreterSettings(), "Settings", "Settings",
                "GA / interpreter settings (population, generations, clusters, metrics, objectives, self-weight, CroSecOpt).",
                GH_ParamAccess.item);                                                                                                   // 3
            pManager.AddTextParameter("Out Folder", "Out",
                "Folder where the JSON file is written. Empty = system temp folder.",
                GH_ParamAccess.item, string.Empty);                                                                                     // 4
            pManager.AddTextParameter("File Tag", "Tag",
                "Optional tag added to the JSON filename (e.g. 'truss_run1').",
                GH_ParamAccess.item, string.Empty);                                                                                     // 5

            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Run summary and progress text.", GH_ParamAccess.item); // 0
            pManager.AddTextParameter("JSON Path", "JSON", "Path of the streamed run JSON.", GH_ParamAccess.item); // 1
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            SG_Shape iniShape = null;
            var rules = new List<SG_Rule>();
            bool reset = false;
            if (!DA.GetData(0, ref iniShape) || iniShape == null) return;
            if (!DA.GetDataList(1, rules)) return;
            DA.GetData(2, ref reset);

            if (!reset)
            {
                DA.SetData(0, "GI_LargeSg idle. Toggle Reset true to run.");
                DA.SetData(1, string.Empty);
                return;
            }

            var settings = DefaultSettings();
            GH_GrammarInterpreterSettings ghSettings = null;
            if (DA.GetData(3, ref ghSettings) && ghSettings?.Value != null)
                settings = CloneSettings(ghSettings.Value);
            settings.Sanitize();

            var feas = new FeasibilitySettings
            {
                WDang = settings.DanglingWeight,
                WAng = settings.AngleWeight,
                WLen = settings.LengthWeight,
                WIntersect = settings.IntersectionWeight,
                WRepet = settings.RepetWeight,
                WDup = settings.DuplicateWeight,
                AngleMinDeg = settings.AngleMinDeg,
                AngleOptDeg = settings.AngleOptDeg,
                LenTooShort = settings.LenTooShort,
                LenOptLow = settings.LenOptLow,
                LenOptHigh = settings.LenOptHigh,
                LenTooLong = settings.LenTooLong
            };

            string outFolder = string.Empty;
            string fileTag = string.Empty;
            DA.GetData(4, ref outFolder);
            DA.GetData(5, ref fileTag);

            var orderedRules = StructuralEvaluator.EnsureInitShapeFirst(rules);
            bool isMultiObjective = settings.NumObjectives > 1;

            var ga = CreateClusterer(settings);
            var moga = CreateMoga(settings);

            List<int> chromosomeLengths = StructuralEvaluator.GetChromosomeLengths(orderedRules, iniShape);
            List<int> ruleMarkers = orderedRules.Select(r => r.RuleMarker).ToList();
            List<GAIndividual> currentPopulation = isMultiObjective
                ? moga.CreateInitialGeneration(settings.PopulationSize, chromosomeLengths, ruleMarkers)
                : ga.CreateInitialGeneration(settings.PopulationSize, chromosomeLengths, ruleMarkers);

            string runId = Guid.NewGuid().ToString("N").Substring(0, 8);
            if (string.IsNullOrWhiteSpace(outFolder)) outFolder = Path.GetTempPath();
            try { Directory.CreateDirectory(outFolder); } catch { outFolder = Path.GetTempPath(); }

            string fileName = string.IsNullOrWhiteSpace(fileTag)
                ? string.Format("GI_LargeSg_{0}.json", runId)
                : string.Format("GI_LargeSg_{0}_{1}.json", SanitizeForFilename(fileTag), runId);
            string jsonPath = Path.Combine(outFolder, fileName);

            var header = new RunHeader
            {
                RunId = runId,
                StartedAt = DateTime.Now,
                Settings = settings,
                Feasibility = feas,
                Rules = orderedRules,
                ChromosomeLengths = chromosomeLengths,
                RuleMarkers = ruleMarkers,
                TopoMetricLabels = settings.TopologyMetrics.Select(TopologyMetrics.GetLabel).ToList(),
                ShapeMetricLabels = settings.ShapeMetrics.Select(ShapeMetrics.GetLabel).ToList(),
                IniShape = iniShape
            };

            var watch = Stopwatch.StartNew();
            string finalPath = string.Empty;
            using (var store = new LargeRunJsonStore())
            {
                try
                {
                    store.Begin(jsonPath, header);

                    for (int gen = 0; gen < settings.Generations; gen++)
                    {
                        bool isLast = gen >= settings.Generations - 1;
                        var outcome = StructuralEvaluator.EvaluatePopulation(
                            currentPopulation,
                            iniShape.DeepCopy(),
                            orderedRules,
                            settings,
                            feas,
                            deepCopyOutputs: false);

                        // Surface rule errors (e.g. wrong marker, 0 struts found) from the first generation.
                        if (gen == 0)
                            foreach (var w in outcome.Warnings)
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

                        var evaluatedPop = outcome.EvaluatedPopulation;
                        ga.ClusterPopulation(evaluatedPop);

                        store.AppendAggregates(evaluatedPop, gen, settings.Clusters);

                        store.BeginGeneration(gen);
                        foreach (var ind in evaluatedPop) store.AppendIndividual(ind);
                        store.EndGeneration();

                        if (!isLast)
                        {
                            if (isMultiObjective)
                            {
                                currentPopulation = moga.ProcessEvaluatedIndividuals(evaluatedPop);
                                moga.IncrementGeneration();
                            }
                            else
                            {
                                currentPopulation = ga.ProcessEvaluatedIndividuals(evaluatedPop);
                                ga.IncrementGeneration();
                            }
                        }
                        else
                        {
                            if (isMultiObjective) moga.ProcessEvaluatedIndividuals(evaluatedPop);
                            else ga.ProcessEvaluatedIndividuals(evaluatedPop);
                        }

                        if (isMultiObjective && settings.ClusterElite > 0 && !isLast)
                            currentPopulation = InjectClusterElites(currentPopulation, evaluatedPop, settings.Clusters, settings.ClusterElite);

                        outcome.Shapes?.Clear();
                        outcome.Models?.Clear();
                    }

                    finalPath = store.Finish();
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GI_LargeSg failed: " + ex.Message);
                    DA.SetData(0, string.Format("GI_LargeSg failed at gen {0}: {1}", store.TotalIndividualsRecorded, ex.Message));
                    DA.SetData(1, string.Empty);
                    return;
                }
            }
            watch.Stop();

            FileInfo fi = null;
            try { fi = new FileInfo(finalPath); } catch { fi = null; }
            string fileSizeStr = fi != null && fi.Exists
                ? string.Format("{0:F1} MB", fi.Length / (1024.0 * 1024.0))
                : "?";

            string info = string.Format(
                "GI_LargeSg run {0}: {1} pop × {2} gen × {3} clusters in {4:F1}s\n" +
                "Individuals streamed: {5}\nJSON: {6} ({7})",
                runId,
                settings.PopulationSize, settings.Generations, settings.Clusters, watch.Elapsed.TotalSeconds,
                settings.PopulationSize * settings.Generations,
                finalPath ?? "(none)", fileSizeStr);

            DA.SetData(0, info);
            DA.SetData(1, finalPath ?? string.Empty);
        }

        // ── helpers ──

        private static GrammarInterpreterSettings DefaultSettings()
        {
            return new GrammarInterpreterSettings
            {
                PopulationSize = 1000,
                Generations = 100,
                Clusters = 10,
                MutationProb = 0.10,
                CrossoverProb = 0.9,
                EliteProb = 0.1,
                TopologyMetrics = new List<int> { 0 },
                ShapeMetrics = new List<int> { 0 },
                GravityDir = new Vector3d(0, 0, -1)
            };
        }

        private static SG_GA CreateClusterer(GrammarInterpreterSettings s)
        {
            return new SG_GA
            {
                PopulationSize = s.PopulationSize,
                NumGenerations = s.Generations,
                NumClusters = s.Clusters,
                MutationProbability = s.MutationProb,
                CrossoverProbability = s.CrossoverProb,
                EliteProbability = s.EliteProb,
                Maximize = MAXIMIZE,
                InitialBoost = 6,
                BlxAlpha = 0.3,
                TopoWeight = s.TopologyWeight,
                ShapeWeight = s.ShapeWeight,
                FitnessWeight = s.FitnessWeight,
                KMeansMaxIterations = s.KMeansIterations,
                ReclusterInterval = s.ReclusterInterval,
                MetricDomains = s.MetricDomains != null && s.MetricDomains.Count > 0 ? new List<Interval>(s.MetricDomains) : null,
                ClusterEliteCount = s.ClusterElite
            };
        }

        private static SG_MOGA CreateMoga(GrammarInterpreterSettings s)
        {
            return new SG_MOGA
            {
                PopulationSize = s.PopulationSize,
                NumGenerations = s.Generations,
                MutationProbability = s.MutationProb,
                CrossoverProbability = s.CrossoverProb,
                EliteProbability = s.EliteProb,
                BlxAlpha = 0.3,
                NumObjectives = s.NumObjectives
            };
        }

        private static List<GAIndividual> InjectClusterElites(List<GAIndividual> offspring, List<GAIndividual> evaluated, int clusters, int count)
        {
            var elites = new List<GAIndividual>();
            for (int c = 0; c < clusters; c++)
                elites.AddRange(evaluated.Where(i => i.ClustGrp == c).OrderBy(i => i.Fitness).Take(count).Select(i => i.Clone()));
            if (elites.Count == 0) return offspring;
            int target = offspring.Count;
            var eliteIds = new HashSet<string>(elites.Select(e => e.Id));
            var result = new List<GAIndividual>(elites);
            result.AddRange(offspring.Where(o => !eliteIds.Contains(o.Id)));
            return result.Take(target).ToList();
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

        private static string SanitizeForFilename(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var bad = Path.GetInvalidFileNameChars();
            var arr = s.Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray();
            return new string(arr);
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;
        public override Guid ComponentGuid => new Guid("C22E4CCB-33A2-4827-B66C-58B0B1437F30");
    }
}
