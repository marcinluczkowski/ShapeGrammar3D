using Grasshopper.Kernel;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Large-run interpreter with wired <see cref="SG_Shape"/> seed and automatic rules.
    /// Streams every individual into a single JSON v2 file (settings, genotypes, objectives,
    /// cluster info, per-cluster aggregates). Exposes JSON path + <see cref="LargeRunContext"/>
    /// for <c>GI_LargeJson Reader</c> rebuilds without re-wiring inputs.
    /// </summary>
    public class GrammarInterpreter_ForLargeModel : GH_Component
    {
        private const bool MAXIMIZE = false;

        public GrammarInterpreter_ForLargeModel()
          : base("Grammar Interpreter Large from SG_Shape", "GI_LargeSg",
              "Large-scale GA with SG_Shape + Autorules. Streams the full run to one JSON file. " +
              "Outputs JSON path and Run Context for GI_LargeJson Reader.",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "Initial SG shape seed.", GH_ParamAccess.item); // 0
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for Automatic Interpreter.", GH_ParamAccess.list); // 1
            pManager.AddBooleanParameter("Reset", "Reset", "Reset & run the large analysis.", GH_ParamAccess.item, false); // 2
            pManager.AddParameter(new Param_GrammarInterpreterSettings(), "Settings", "Settings",
                "GA / interpreter settings (population, generations, clusters, metrics, objectives, self-weight, CroSecOpt).",
                GH_ParamAccess.item); // 3
            pManager.AddTextParameter("JSON Folder", "JsonOut",
                "Folder only, or full path ending in .json (full path ignores JSON File). Empty folder = system temp.",
                GH_ParamAccess.item, string.Empty); // 4
            pManager.AddTextParameter("JSON File", "JsonFile",
                "File name (e.g. myrun.json). Empty = GI_LargeSg.json. Same path overwrites existing file.",
                GH_ParamAccess.item, string.Empty); // 5

            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("JSON Path", "JSON", "Path of the streamed run JSON (v2). Wire to GI_LargeJson Reader.", GH_ParamAccess.item); // 0
            pManager.AddParameter(new Param_LargeRunContext(), "Run Context", "RunCtx",
                "SG_Shape seed + ordered rules for this run. Wire to GI_LargeJson Reader to rebuild models.",
                GH_ParamAccess.item); // 1
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            SG_Shape iniShape = null;
            var rules = new List<SG_Rule>();
            bool reset = false;
            if (!DA.GetData(0, ref iniShape) || iniShape == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "SG_Shape input is required.");
                DA.SetData(0, string.Empty);
                DA.SetData(1, null);
                return;
            }

            if (!DA.GetDataList(1, rules)) return;
            DA.GetData(2, ref reset);

            var idleOrderedRules = StructuralEvaluator.EnsureInitShapeFirst(rules);
            var idleContext = new LargeRunContext
            {
                IniShape = iniShape,
                Rules = idleOrderedRules,
                JsonPath = string.Empty,
                RunId = string.Empty
            };

            if (!reset)
            {
                DA.SetData(0, string.Empty);
                DA.SetData(1, new GH_LargeRunContext(idleContext));
                return;
            }

            var settings = DefaultSettings();
            GH_GrammarInterpreterSettings ghSettings = null;
            if (DA.GetData(3, ref ghSettings) && ghSettings?.Value != null)
                settings = CloneSettings(ghSettings.Value);
            settings.Sanitize();

            string jsonFolderOrFile = string.Empty;
            DA.GetData(4, ref jsonFolderOrFile);
            string jsonFileName = string.Empty;
            DA.GetData(5, ref jsonFileName);

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

            var orderedRules = StructuralEvaluator.EnsureInitShapeFirst(rules);
            bool isMultiObjective = settings.NumObjectives > 1;

            var ga = CreateClusterer(settings);
            var moga = CreateMoga(settings);

            List<int> chromosomeLengths = StructuralEvaluator.GetChromosomeLengths(orderedRules, iniShape);
            List<int> ruleMarkers = orderedRules.Select(r => r.RuleMarker).ToList();

            FixedGenotypes.Reset();

            List<GAIndividual> currentPopulation;
            if (settings.FixedSeed)
            {
                currentPopulation = FixedGenotypes.Get(settings.PopulationSize, chromosomeLengths, ruleMarkers);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    string.Format("Using fixed-seed population ({0} individuals)", currentPopulation.Count));
            }
            else if (isMultiObjective)
                currentPopulation = moga.CreateInitialGeneration(settings.PopulationSize, chromosomeLengths, ruleMarkers);
            else
                currentPopulation = ga.CreateInitialGeneration(settings.PopulationSize, chromosomeLengths, ruleMarkers);

            string runId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string jsonPath = GrammarInterpreterJsonPath.Resolve(jsonFolderOrFile, jsonFileName, "GI_LargeSg.json");

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
                            deepCopyOutputs: false,
                            collectOutputs: false);

                        if (gen == 0)
                        {
                            foreach (var w in outcome.Warnings)
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
                            foreach (var r in outcome.Remarks)
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, r);
                        }

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

                        if (isMultiObjective && settings.ClusterElite > 0 && !isLast)
                            currentPopulation = InjectClusterElites(currentPopulation, evaluatedPop, settings.Clusters, settings.ClusterElite);

                        ReleaseGenerationReferences(outcome, evaluatedPop, gen, isLast);
                    }

                    finalPath = store.Finish();
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GI_LargeSg failed: " + ex.Message);
                    DA.SetData(0, string.Empty);
                    DA.SetData(1, new GH_LargeRunContext(new LargeRunContext
                    {
                        IniShape = iniShape,
                        Rules = orderedRules,
                        JsonPath = string.Empty,
                        RunId = runId
                    }));
                    return;
                }
            }
            watch.Stop();

            DA.SetData(0, finalPath ?? string.Empty);
            DA.SetData(1, new GH_LargeRunContext(new LargeRunContext
            {
                IniShape = iniShape,
                Rules = orderedRules,
                JsonPath = finalPath ?? string.Empty,
                RunId = runId
            }));

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                string.Format("GI_LargeSg run {0} finished in {1:F1}s — {2}",
                    runId, watch.Elapsed.TotalSeconds, finalPath ?? "(none)"));
        }

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
                ClusterEliteCount = s.ClusterElite,
                RetainEvaluatedHistory = false
            };
        }

        private static void ReleaseGenerationReferences(
            StructuralEvaluator.EvaluationOutcome outcome,
            List<GAIndividual> evaluatedPop,
            int generation,
            bool isLast)
        {
            outcome?.Shapes?.Clear();
            outcome?.Models?.Clear();
            outcome?.Warnings?.Clear();
            outcome?.Remarks?.Clear();
            evaluatedPop?.Clear();

            if (isLast || generation % 5 == 4)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
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

        protected override Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("A7B8C9D0-E1F2-4A3B-8C5D-6E7F8A9B0C1D");
    }
}
