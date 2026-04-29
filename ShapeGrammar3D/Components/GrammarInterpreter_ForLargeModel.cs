using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using System.Drawing;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GrammarInterpreter_ForLargeModel : GH_Component
    {
        // Genetic Algorithm configuration (overridable from GH inputs). Defaults tuned for the headline
        // large-scale workflow: 1000 individuals × 100 generations × 10 clusters.
        private int _populationSize = 1000;
        private int _numGenerations = 100;
        private int _numClusters = 10;
        private double _mutationProbability = 0.10;
        private double _crossoverProbability = 0.9;
        private double _eliteProbability = 0.1;
        private const bool MAXIMIZE = false; // Minimize displacement

        // Clustering configuration
        private double _topoWeight = 1.0;
        private double _shapeWeight = 1.0;
        private double _fitnessWeight = 0.0;
        private int _kmeansIterations = 10;
        private int _reclusterInterval = 5;
        private List<int> _topoMetricTypes = new List<int> { 0 };
        private List<int> _shapeMetricTypes = new List<int> { 0 };
        private double _shapeShrinkWrapDetailRatio = ShapeMetrics.DefaultShrinkWrapDetailRatio;

        // Feasibility configuration
        private FeasibilitySettings _feasibilitySettings = FeasibilitySettings.Default();

        // Cluster elite: guaranteed survivors per cluster per generation
        private int _clusterEliteCount = 0;

        // Multi-objective configuration
        private int _numObjectives = 1;
        private int _singleObjType = 0;  // 0=Disp, 1=Feas, 2=AvgUtilDev, 3=MaxUtil
        private int _utilObjType = 0;    // 0=AvgDev, 1=MaxUtil

        // Self-weight
        private bool _useSelfWeight = false;
        private Vector3d _gravityDir = new Vector3d(0, 0, -1);

        // Cross-section optimization: 0=off, 1=Rect, 2=SHS catalog
        private int _croSecOptMode = 0;
        private int _croSecMaxIter = 40;

        // User-supplied normalization domains (one per metric dimension)
        private List<Rhino.Geometry.Interval> _metricDomains = null;

        private SG_GA _ga;
        private SG_MOGA _moga;
        private int _currentGeneration;
        private List<GAIndividual> _currentPopulation;
        // Memory-optimized: only first and last generation shapes stored with cluster+fitness for filtering.
        private List<(SG_Shape Shape, int Cluster, double Fitness)> _firstGenData;
        private List<(SG_Shape Shape, int Cluster, double Fitness)> _lastGenData;
        /// <summary>Top-1 individual per cluster per generation: shape+model+cluster+fitness+gen.</summary>
        private List<(int Generation, int Cluster, SG_Shape Shape, TB_Model Model, double Fitness, string Id)> _topPerGenCluster;
        /// <summary>Per generation, per cluster: (gen, cluster, best, worst, avg) fitness for convergence plots.</summary>
        private List<(int gen, int cluster, double best, double worst, double avg)> _convergenceData;
        /// <summary>Streaming CSV+JSON recorder for lightweight per-individual scalar metrics.</summary>
        private LargeRunStore _runStore;
        /// <summary>Per-stage timing accumulators for the first generation (diagnostic only).</summary>
        private Dictionary<string, double> _stageTimingsMs;

        private const double CONV_TEXT_HEIGHT = 0.12;

        /// <summary>
        /// Large-scale GA interpreter, predefined for high-throughput runs (e.g. 1000 pop × 100 gen × 10 clusters).
        /// Inputs match GI_FromBnd: only Automatic Rules + Reset + Settings (no SG_Shape; first rule must be AutoRule_InitShape_3D).
        ///
        /// Memory strategy (radically reduced vs. GI_FromBnd):
        ///   • Per individual: scalar metrics only (fitness, displacement, utilisation, feasibility, topo/shape)
        ///     are streamed to a CSV file row-by-row so RAM stays bounded for 1M+ records.
        ///   • Geometry kept in-memory: ONLY the random first generation, the final converged generation,
        ///     and the top-1 individual per cluster per generation (≈ numClusters × numGenerations shapes).
        ///   • Convergence aggregates (best/worst/avg per cluster per gen) live in a small JSON sidecar.
        /// </summary>
        public GrammarInterpreter_ForLargeModel()
          : base("Grammar Interpreter for Large Model", "GI_Large",
              "Large-scale GA (default 1000×100×10). Streams scalar metrics for every individual to CSV, " +
              "stores geometry only for first+last generations and the top-1 individual per cluster per generation.",
              UT.CAT, UT.GR_INT)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// Inputs intentionally mirror GI_FromBnd (no SG_Shape) so this component can be a drop-in
        /// replacement for the FromBoundary interpreter when scaling up to 1000 × 100 runs.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Automatic Rules", "Autorules",
                "Rules for Automatic Interpreter (first rule must be AutoRule_InitShape_3D)",
                GH_ParamAccess.list);                                                                                                     // 0
            pManager.AddBooleanParameter("Reset", "Reset", "Reset & re-run the genetic algorithm", GH_ParamAccess.item, false);            // 1
            pManager.AddParameter(new Param_GrammarInterpreterSettings(), "Settings", "Settings",
                "All GA/interpreter analysis settings packed in one object from GI_Settings component",
                GH_ParamAccess.item);                                                                                                     // 2
            pManager.AddNumberParameter("% Top", "Pct",
                "Percent of top individuals per cluster to expose on First/Last outputs (100 = all, 10 = top 10% per cluster).",
                GH_ParamAccess.item, 100.0);                                                                                              // 3
            pManager.AddPointParameter("Insert Pt", "Pt", "Base point for convergence graph",
                GH_ParamAccess.item, Point3d.Origin);                                                                                     // 4
            pManager.AddNumberParameter("Graph W", "dX", "Convergence graph width in model units",
                GH_ParamAccess.item, 15.0);                                                                                               // 5
            pManager.AddNumberParameter("Graph H", "dY", "Convergence graph height in model units",
                GH_ParamAccess.item, 8.0);                                                                                                // 6
            pManager.AddTextParameter("Out Folder", "Out",
                "Folder where the per-individual CSV and the JSON summary are written. Empty = system temp folder.",
                GH_ParamAccess.item, string.Empty);                                                                                       // 7
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info",
                "GA run summary + cumulative per-stage and per-metric timings across the whole run (sorted by cost).",
                GH_ParamAccess.item);                                                                                                                // 0
            pManager.AddGenericParameter("First Gen", "First", "SG_Shape list of the random first generation (filtered by % per cluster)",
                GH_ParamAccess.list);                                                                                                                // 1
            pManager.AddGenericParameter("Last Gen", "Last", "SG_Shape list of the final optimised generation (filtered by % per cluster)",
                GH_ParamAccess.list);                                                                                                                // 2
            pManager.AddGenericParameter("Top per Gen-Cluster", "Top",
                "Tree of best SG_Shape per cluster per generation. Path = {generation}, item index = cluster id.",
                GH_ParamAccess.tree);                                                                                                                // 3
            pManager.AddColourParameter("First Colours", "FCol", "Cluster colour per First Gen shape", GH_ParamAccess.list);                         // 4
            pManager.AddColourParameter("Last Colours", "LCol", "Cluster colour per Last Gen shape", GH_ParamAccess.list);                           // 5
            pManager.AddColourParameter("Top Colours", "TCol", "Cluster colours matching Top per Gen-Cluster tree",
                GH_ParamAccess.tree);                                                                                                                // 6
            pManager.AddNumberParameter("Conv Best", "CBest", "Convergence best per cluster per gen. Path = (gen, cluster)",
                GH_ParamAccess.tree);                                                                                                                // 7
            pManager.AddNumberParameter("Conv Worst", "CWorst", "Convergence worst per cluster per gen", GH_ParamAccess.tree);                       // 8
            pManager.AddNumberParameter("Conv Avg", "CAvg", "Convergence average per cluster per gen", GH_ParamAccess.tree);                         // 9
            pManager.AddLineParameter("Conv Lines", "CLn", "Convergence graph lines (axes, grid, curves)", GH_ParamAccess.tree);                     // 10
            pManager.AddColourParameter("Conv Colours", "CCol", "Colours for Conv Lines", GH_ParamAccess.tree);                                      // 11
            pManager.AddTextParameter("CSV Path", "CSV",
                "Absolute path to the streamed per-individual CSV (one row per individual per generation).",
                GH_ParamAccess.item);                                                                                                                // 12
            pManager.AddTextParameter("JSON Path", "JSON",
                "Absolute path to the JSON summary (run metadata + per-cluster aggregates per generation).",
                GH_ParamAccess.item);                                                                                                                // 13
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly",
                "Lightweight assembly: only the geometry kept in memory (first gen, last gen, top-1 per cluster per gen).",
                GH_ParamAccess.item);                                                                                                                // 14
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _ga = null;
            _currentGeneration = 0;
            _currentPopulation = null;
            _firstGenData = null;
            _lastGenData = null;
            _topPerGenCluster = new List<(int, int, SG_Shape, TB_Model, double, string)>();
            _convergenceData = new List<(int gen, int cluster, double best, double worst, double avg)>();
            _stageTimingsMs = new Dictionary<string, double>();

            List<SG_Rule> rls = new List<SG_Rule>();
            bool reset = false;

            if (!DA.GetDataList(0, rls)) return;
            if (!DA.GetData(1, ref reset)) return;

            if (!rls.OfType<SG_AutoRule_InitShape_3D>().Any())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The first rule must be AutoRule_InitShape_3D (no SG_Shape input is provided).");
                return;
            }

            if (!reset)
            {
                DA.SetData(0, "GA idle. Toggle Reset (true) to (re)run.");
                EmitEmptyOutputs(DA);
                return;
            }

            // --- Settings bundle ---
            var settings = new GrammarInterpreterSettings
            {
                PopulationSize = _populationSize,
                Generations = _numGenerations,
                Clusters = _numClusters,
                MutationProb = _mutationProbability,
                CrossoverProb = _crossoverProbability,
                EliteProb = _eliteProbability,
                TopologyWeight = _topoWeight,
                ShapeWeight = _shapeWeight,
                FitnessWeight = _fitnessWeight,
                KMeansIterations = _kmeansIterations,
                ReclusterInterval = _reclusterInterval,
                TopologyMetrics = new List<int>(_topoMetricTypes),
                ShapeMetrics = new List<int>(_shapeMetricTypes),
                ShapeShrinkWrapDetailRatio = _shapeShrinkWrapDetailRatio,
                FixedSeed = false,
                DanglingWeight = _feasibilitySettings.WDang,
                AngleWeight = _feasibilitySettings.WAng,
                LengthWeight = _feasibilitySettings.WLen,
                IntersectionWeight = _feasibilitySettings.WIntersect,
                AngleMinDeg = _feasibilitySettings.AngleMinDeg,
                AngleOptDeg = _feasibilitySettings.AngleOptDeg,
                LenTooShort = _feasibilitySettings.LenTooShort,
                LenOptLow = _feasibilitySettings.LenOptLow,
                LenOptHigh = _feasibilitySettings.LenOptHigh,
                LenTooLong = _feasibilitySettings.LenTooLong,
                NumObjectives = _numObjectives,
                SingleObjType = _singleObjType,
                UtilObjType = _utilObjType,
                SelfWeight = _useSelfWeight,
                CroSecOpt = _croSecOptMode,
                MetricDomains = _metricDomains != null ? new List<Interval>(_metricDomains) : new List<Interval>(),
                GravityDir = _gravityDir,
                ClusterElite = _clusterEliteCount,
                CSOptIterations = _croSecMaxIter
            };

            GH_GrammarInterpreterSettings ghSettings = null;
            if (DA.GetData(2, ref ghSettings) && ghSettings?.Value != null)
                settings = ghSettings.Value;
            settings.Sanitize();

            _populationSize = settings.PopulationSize;
            _numGenerations = settings.Generations;
            _numClusters = settings.Clusters;
            _mutationProbability = settings.MutationProb;
            _crossoverProbability = settings.CrossoverProb;
            _eliteProbability = settings.EliteProb;

            _topoWeight = settings.TopologyWeight;
            _shapeWeight = settings.ShapeWeight;
            _fitnessWeight = settings.FitnessWeight;
            _kmeansIterations = settings.KMeansIterations;
            _reclusterInterval = settings.ReclusterInterval;

            _topoMetricTypes = settings.TopologyMetrics
                .Select(v => Math.Clamp(v, 0, TopologyMetrics.Count - 1))
                .Distinct().ToList();
            _shapeMetricTypes = settings.ShapeMetrics
                .Select(v => Math.Clamp(v, 0, ShapeMetrics.Count - 1))
                .Distinct().ToList();
            _shapeShrinkWrapDetailRatio = settings.ShapeShrinkWrapDetailRatio;

            bool useFixedSeed = settings.FixedSeed;

            _feasibilitySettings = new FeasibilitySettings
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
            _numObjectives = settings.NumObjectives;
            _singleObjType = settings.SingleObjType;
            _utilObjType = settings.UtilObjType;
            bool isMultiObjective = _numObjectives > 1;

            var initRuleForSelfWeight = rls.OfType<SG_AutoRule_InitShape_3D>().FirstOrDefault();
            _useSelfWeight = initRuleForSelfWeight?.UseSelfWeight ?? false;
            _croSecOptMode = settings.CroSecOpt;
            _metricDomains = settings.MetricDomains != null && settings.MetricDomains.Count > 0
                ? new List<Interval>(settings.MetricDomains)
                : null;
            _clusterEliteCount = settings.ClusterElite;
            _croSecMaxIter = settings.CSOptIterations;
            _gravityDir = settings.GravityDir;

            // --- init GA ---
            InitializeGA();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "GA initialized");

            rls = EnsureInitShapeFirst(rls);

            // The init rule builds the initial shape from scratch; we just provide an empty SG_Shape.
            var ini_Shape = new SG_Shape
            {
                Nodes = new List<SG_Node>(),
                Supports = new List<SG_Support>(),
                PointLoads = new List<SG_PointLoad>(),
                LineLoads = new List<SG_LineLoad>()
            };

            if (_currentPopulation == null)
            {
                List<int> chromosomeLengths = GetChromosomeLengths(rls, ini_Shape);
                List<int> ruleMarkers = rls.Select(r => r.RuleMarker).ToList();

                if (useFixedSeed)
                {
                    _currentPopulation = FixedGenotypes.Get(_populationSize, chromosomeLengths, ruleMarkers);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        string.Format("Using fixed-seed population ({0} individuals)", _currentPopulation.Count));
                }
                else if (isMultiObjective)
                {
                    _currentPopulation = _moga.CreateInitialGeneration(_populationSize, chromosomeLengths, ruleMarkers);
                }
                else
                {
                    _currentPopulation = _ga.CreateInitialGeneration(_populationSize, chromosomeLengths, ruleMarkers);
                }
            }

            double pctTop = 100.0;
            Point3d insertPt = Point3d.Origin;
            double graphW = 15.0, graphH = 8.0;
            string outFolder = string.Empty;
            DA.GetData(3, ref pctTop);
            DA.GetData(4, ref insertPt);
            DA.GetData(5, ref graphW);
            DA.GetData(6, ref graphH);
            DA.GetData(7, ref outFolder);
            pctTop = Math.Clamp(pctTop, 0.01, 100.0);
            graphW = Math.Max(1.0, graphW);
            graphH = Math.Max(1.0, graphH);

            // --- streaming output paths ---
            string runId = Guid.NewGuid().ToString("N").Substring(0, 8);
            if (string.IsNullOrWhiteSpace(outFolder)) outFolder = Path.GetTempPath();
            try { Directory.CreateDirectory(outFolder); } catch { /* fall back to temp */ outFolder = Path.GetTempPath(); }
            string csvPath = Path.Combine(outFolder, string.Format("GI_Large_{0}_individuals.csv", runId));
            string jsonPath = Path.Combine(outFolder, string.Format("GI_Large_{0}_summary.json", runId));

            _runStore = new LargeRunStore
            {
                RunId = runId,
                StartedAt = DateTime.Now,
                PopulationSize = _populationSize,
                NumGenerations = _numGenerations,
                NumClusters = _numClusters,
                NumObjectives = _numObjectives,
                MutationProb = _mutationProbability,
                CrossoverProb = _crossoverProbability,
                EliteProb = _eliteProbability,
                UseSelfWeight = _useSelfWeight,
                CroSecOptMode = _croSecOptMode,
                TopoMetricTypes = new List<int>(_topoMetricTypes),
                ShapeMetricTypes = new List<int>(_shapeMetricTypes),
                TopoMetricLabels = _topoMetricTypes.Select(TopologyMetrics.GetLabel).ToList(),
                ShapeMetricLabels = _shapeMetricTypes.Select(ShapeMetrics.GetLabel).ToList()
            };
            _runStore.BeginCsv(csvPath);

            List<GAIndividual> evaluatedPop = null;
            string lastClusterLog = string.Empty;
            var assembly = new SGShapeGrammar3DAssembly
            {
                Config = new AssemblyConfig
                {
                    PopulationSize = _populationSize,
                    NumGenerations = _numGenerations,
                    NumClusters = _numClusters,
                    NumObjectives = _numObjectives,
                    TopoMetricTypes = new List<int>(_topoMetricTypes),
                    ShapeMetricTypes = new List<int>(_shapeMetricTypes),
                    FeasibilityAngleMinDeg = _feasibilitySettings.AngleMinDeg,
                    FeasibilityAngleOptDeg = _feasibilitySettings.AngleOptDeg,
                    FeasibilityLenTooShort = _feasibilitySettings.LenTooShort,
                    FeasibilityLenOptLow = _feasibilitySettings.LenOptLow,
                    FeasibilityLenOptHigh = _feasibilitySettings.LenOptHigh,
                    FeasibilityLenTooLong = _feasibilitySettings.LenTooLong
                }
            };
            foreach (int t in _topoMetricTypes)
                assembly.MetricNames.Add("T:" + TopologyMetrics.GetLabel(t));
            foreach (int s in _shapeMetricTypes)
                assembly.MetricNames.Add("S:" + ShapeMetrics.GetLabel(s));

            var totalRunWatch = Stopwatch.StartNew();

            // Hook the FEM and boundary-check internal sub-stage profiling into our
            // run-wide timing dictionary. Stopwatch overhead is negligible compared
            // to the FEM / Brep / Mesh calls being measured.
            SolveLS.ProfileMsAccumulator = _stageTimingsMs;
            BoundaryConstraintUtil.ProfileMsAccumulator = _stageTimingsMs;

            try
            {

            while (true)
            {
                int generationId = _currentGeneration;
                bool isLastGeneration = _currentGeneration >= _numGenerations - 1;
                bool keepFullGeometry = (_currentGeneration == 0) || isLastGeneration;

                // Reuse a single empty initial shape — RuleOps build everything from scratch each time.
                SG_Shape deepCopiedIniShape = CloneShape(ini_Shape);

                List<SG_Shape> evaluatedShapes;
                List<TB_Model> evaluatedModels;
                evaluatedPop = EvaluatePopulation(
                    _currentPopulation, deepCopiedIniShape, rls,
                    keepFullGeometry: keepFullGeometry,
                    keepTopPerCluster: !keepFullGeometry,  // for non-first/last gens we still need top-1/cluster
                    out evaluatedShapes, out evaluatedModels);

                // Cluster so ClustGrp is set before convergence + recording (both SO and MO)
                _ga.ClusterPopulation(evaluatedPop);

                AppendConvergenceForGeneration(evaluatedPop, _currentGeneration, _numClusters);
                _runStore.AppendAggregates(evaluatedPop, _currentGeneration, _numClusters);
                _runStore.AppendIndividuals(evaluatedPop, _currentGeneration);

                // Snapshot first/last full generation geometry (top % per cluster filtering happens on output)
                if (_currentGeneration == 0)
                    _firstGenData = SnapshotGenerationGeometry(evaluatedShapes, evaluatedPop);
                if (isLastGeneration)
                    _lastGenData = SnapshotGenerationGeometry(evaluatedShapes, evaluatedPop);

                // Top-1 per cluster per generation (for the Top per Gen-Cluster output)
                AppendTopPerCluster(evaluatedPop, evaluatedShapes, evaluatedModels, _currentGeneration, _numClusters);

                lastClusterLog = isMultiObjective
                    ? BuildMOInfo(evaluatedPop, _currentGeneration)
                    : BuildClusterInfo(evaluatedPop, _currentGeneration);

                if (!isLastGeneration)
                {
                    if (isMultiObjective)
                    {
                        _currentPopulation = _moga.ProcessEvaluatedIndividuals(evaluatedPop);
                        _moga.IncrementGeneration();
                        _currentGeneration = _moga.CurrentGeneration;
                    }
                    else
                    {
                        _currentPopulation = _ga.ProcessEvaluatedIndividuals(evaluatedPop);
                        _ga.IncrementGeneration();
                        _currentGeneration = _ga.CurrentGeneration;
                    }
                }
                else
                {
                    if (isMultiObjective)
                        _moga.ProcessEvaluatedIndividuals(evaluatedPop);
                    else
                        _ga.ProcessEvaluatedIndividuals(evaluatedPop);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        string.Format("Completed all {0} generations ({1})",
                            _numGenerations, isMultiObjective ? "NSGA-II" : "single-objective"));
                }

                if (isMultiObjective)
                    _ga.ClusterPopulation(evaluatedPop); // MOGA: re-cluster for elite selection

                if (isMultiObjective && _clusterEliteCount > 0 && !isLastGeneration)
                {
                    _currentPopulation = InjectClusterElites(
                        _currentPopulation, evaluatedPop, _numClusters, _clusterEliteCount);
                }

                // Aggressively drop the per-individual lists; only the deep-copied snapshots we explicitly
                // kept in _firstGenData / _lastGenData / _topPerGenCluster live past this iteration.
                evaluatedShapes?.Clear();
                evaluatedModels?.Clear();
                evaluatedShapes = null;
                evaluatedModels = null;

                if (isLastGeneration)
                    break;
            }

            }
            finally
            {
                // Always release the FEM profiling hook so unrelated solver calls
                // (other components, future GH solutions) don't accidentally write
                // into a stale dictionary.
                SolveLS.ProfileMsAccumulator = null;
                BoundaryConstraintUtil.ProfileMsAccumulator = null;
            }

            totalRunWatch.Stop();

            _runStore.FinishedAt = DateTime.Now;
            _runStore.EndCsv();
            string savedJsonPath = _runStore.SaveJsonSummary(jsonPath);

            // Build the LIGHTWEIGHT assembly: only the kept geometry (top-1 per cluster per gen + first + last).
            // This is the primary speed-up for the assembly construction step (was deep-copying every individual).
            BuildLightweightAssembly(assembly);

            OutputFirstLastAndConvergence(
                DA, lastClusterLog, pctTop, insertPt, graphW, graphH, assembly,
                csvPath, savedJsonPath, totalRunWatch.Elapsed.TotalSeconds);

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                string.Format("GA completed {0} generations in {1:F1}s. CSV: {2}",
                    _numGenerations, totalRunWatch.Elapsed.TotalSeconds, csvPath));
        }

        /// <summary>
        /// Emits empty values for every output. Used on the idle (Reset == false) path so
        /// downstream components don't see stale data.
        /// </summary>
        private void EmitEmptyOutputs(IGH_DataAccess DA)
        {
            DA.SetDataList(1, new List<SG_Shape>());
            DA.SetDataList(2, new List<SG_Shape>());
            DA.SetDataTree(3, new GH_Structure<IGH_Goo>());
            DA.SetDataList(4, new List<Color>());
            DA.SetDataList(5, new List<Color>());
            DA.SetDataTree(6, new GH_Structure<GH_Colour>());
            DA.SetDataTree(7, new GH_Structure<GH_Number>());
            DA.SetDataTree(8, new GH_Structure<GH_Number>());
            DA.SetDataTree(9, new GH_Structure<GH_Number>());
            DA.SetDataTree(10, new GH_Structure<GH_Line>());
            DA.SetDataTree(11, new GH_Structure<GH_Colour>());
            DA.SetData(12, string.Empty);
            DA.SetData(13, string.Empty);
            DA.SetData(14, new GH_SGAssembly(new SGShapeGrammar3DAssembly()));
        }

        /// <summary>
        /// Deep-copies the (already-cloned) shapes from a generation and pairs them with their
        /// cluster + fitness, ready for the FilterTopPercent step at output time.
        /// </summary>
        private static List<(SG_Shape, int, double)> SnapshotGenerationGeometry(
            List<SG_Shape> shapes, List<GAIndividual> pop)
        {
            var result = new List<(SG_Shape, int, double)>();
            int n = Math.Min(shapes?.Count ?? 0, pop?.Count ?? 0);
            for (int i = 0; i < n; i++)
                if (shapes[i] != null)
                    result.Add((shapes[i], pop[i].ClustGrp, pop[i].Fitness));
            return result;
        }

        /// <summary>
        /// Selects the top-1 individual per cluster for this generation and stores its
        /// (already deep-copied) shape + model in <see cref="_topPerGenCluster"/>.
        /// Skips clusters that had no successful individual.
        /// </summary>
        private void AppendTopPerCluster(
            List<GAIndividual> pop, List<SG_Shape> shapes, List<TB_Model> models,
            int generation, int numClusters)
        {
            if (pop == null || pop.Count == 0) return;
            int n = pop.Count;
            for (int c = 0; c < Math.Max(1, numClusters); c++)
            {
                int bestIdx = -1;
                double bestFit = MAXIMIZE ? double.MinValue : double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (pop[i].ClustGrp != c) continue;
                    if (shapes != null && i < shapes.Count && shapes[i] == null) continue; // failed eval
                    double f = pop[i].Fitness;
                    if (double.IsNaN(f) || double.IsInfinity(f) || f == double.MaxValue || f == double.MinValue) continue;
                    bool better = MAXIMIZE ? f > bestFit : f < bestFit;
                    if (better) { bestFit = f; bestIdx = i; }
                }

                if (bestIdx < 0) continue;

                SG_Shape shape = (shapes != null && bestIdx < shapes.Count) ? shapes[bestIdx] : null;
                TB_Model mdl = (models != null && bestIdx < models.Count) ? models[bestIdx] : null;
                if (shape == null) continue;

                _topPerGenCluster.Add((generation, c, shape, mdl, bestFit, pop[bestIdx].Id));
            }
        }

        /// <summary>
        /// Populates <paramref name="assembly"/> with only the kept geometry: one
        /// AssemblyGeneration per recorded generation containing the top-1 per cluster.
        /// First and last generation also include their full population geometry. This
        /// keeps the assembly object small enough for downstream Data Preview components
        /// even when the GA evaluated 100k+ individuals.
        /// </summary>
        private void BuildLightweightAssembly(SGShapeGrammar3DAssembly assembly)
        {
            if (_topPerGenCluster == null) return;
            var byGen = _topPerGenCluster.GroupBy(t => t.Generation).OrderBy(g => g.Key);
            foreach (var grp in byGen)
            {
                var ag = new AssemblyGeneration { Generation = grp.Key };
                foreach (var (_, cluster, shape, mdl, fitness, id) in grp.OrderBy(x => x.Cluster))
                {
                    var ind = new GAIndividual(new List<int>(), new List<double>(), id ?? "?")
                    {
                        Fitness = fitness,
                        ClustGrp = cluster
                    };
                    ag.Individuals.Add(AssemblyIndividual.FromGAIndividual(ind, mdl, shape));
                }
                assembly.Generations.Add(ag);
            }
        }

        /// <summary>Appends best/worst/avg fitness per cluster for this generation to _convergenceData.</summary>
        private void AppendConvergenceForGeneration(List<GAIndividual> population, int generation, int numClusters)
        {
            if (population == null) return;
            var validFitness = population
                .Where(ind => !double.IsInfinity(ind.Fitness) && ind.Fitness != double.MaxValue && ind.Fitness != double.MinValue)
                .ToList();
            for (int c = 0; c < numClusters; c++)
            {
                var inCluster = validFitness.Where(ind => ind.ClustGrp == c).ToList();
                double best = inCluster.Count > 0 ? inCluster.Min(ind => ind.Fitness) : double.NaN;
                double worst = inCluster.Count > 0 ? inCluster.Max(ind => ind.Fitness) : double.NaN;
                double avg = inCluster.Count > 0 ? inCluster.Average(ind => ind.Fitness) : double.NaN;
                _convergenceData.Add((generation, c, best, worst, avg));
            }
        }

        /// <summary>
        /// Builds a summary string for cluster distribution at a given generation.
        /// </summary>
        private string BuildClusterInfo(List<GAIndividual> population, int generation)
        {
            var grouped = population.GroupBy(ind => ind.ClustGrp).OrderBy(g => g.Key);
            var parts = new List<string>();
            parts.Add(string.Format("Gen {0}:", generation));

            foreach (var grp in grouped)
            {
                var validFitness = grp
                    .Where(ind => !double.IsInfinity(ind.Fitness) && ind.Fitness != double.MaxValue && ind.Fitness != double.MinValue)
                    .ToList();

                string bestStr = validFitness.Count > 0
                    ? validFitness.Min(ind => ind.Fitness).ToString("E3")
                    : "N/A";

                double avgTopo = grp.Average(ind => ind.Topo);
                double avgShpe = grp.Average(ind => ind.Shpe);
                double avgFeas = grp.Average(ind => ind.Feas);

                parts.Add(string.Format(
                    "  Cluster {0}: n={1}, BestFit={2}, AvgTopo={3:F1}, AvgShpe={4:F1}, AvgFeas={5:F4}",
                    grp.Key, grp.Count(), bestStr, avgTopo, avgShpe, avgFeas));
            }

            return string.Join("\n", parts);
        }

        /// <summary>
        /// Builds a summary string for multi-objective NSGA-II at a given generation.
        /// </summary>
        private string BuildMOInfo(List<GAIndividual> population, int generation)
        {
            int frontZeroCount = population.Count(ind => ind.Rank == 0);
            int maxRank = population.Max(ind => ind.Rank);

            var validDisp = population
                .Where(ind => ind.ObjectiveValues.Count > 0
                    && ind.ObjectiveValues[0] < double.MaxValue)
                .ToList();

            string bestDisp = validDisp.Count > 0
                ? validDisp.Min(ind => ind.ObjectiveValues[0]).ToString("F4")
                : "N/A";
            string bestUtil = validDisp.Count > 0 && validDisp[0].ObjectiveValues.Count > 1
                ? validDisp.Min(ind => ind.ObjectiveValues[1]).ToString("F4")
                : "N/A";

            string bestFeas = validDisp.Count > 0 && validDisp[0].ObjectiveValues.Count > 2
                ? validDisp.Min(ind => ind.ObjectiveValues[2]).ToString("F4")
                : "N/A";

            return string.Format(
                "Gen {0} [NSGA-II, {1} obj]: Front0={2}, MaxRank={3}, " +
                "BestDisp(log)={4}, BestUtilDev={5}, BestFeas={6}",
                generation, _numObjectives, frontZeroCount, maxRank,
                bestDisp, bestUtil, bestFeas);
        }

        /// <summary>
        /// Initializes the genetic algorithm with current parameters.
        /// </summary>
        private void InitializeGA()
        {
            _ga = new SG_GA
            {
                PopulationSize = _populationSize,
                NumGenerations = _numGenerations,
                NumClusters = _numClusters,
                MutationProbability = _mutationProbability,
                CrossoverProbability = _crossoverProbability,
                EliteProbability = _eliteProbability,
                Maximize = MAXIMIZE,
                InitialBoost = 6,
                BlxAlpha = 0.3,
                TopoWeight = _topoWeight,
                ShapeWeight = _shapeWeight,
                FitnessWeight = _fitnessWeight,
                KMeansMaxIterations = _kmeansIterations,
                ReclusterInterval = _reclusterInterval,
                MetricDomains = _metricDomains,
                ClusterEliteCount = _clusterEliteCount
            };

            _moga = new SG_MOGA
            {
                PopulationSize = _populationSize,
                NumGenerations = _numGenerations,
                MutationProbability = _mutationProbability,
                CrossoverProbability = _crossoverProbability,
                EliteProbability = _eliteProbability,
                BlxAlpha = 0.3,
                NumObjectives = _numObjectives
            };

            _currentGeneration = 0;
            _currentPopulation = null;
            _firstGenData = null;
            _lastGenData = null;
            _convergenceData = new List<(int gen, int cluster, double best, double worst, double avg)>();

            FixedGenotypes.Reset();
        }

        /// <summary>
        /// Injects the best N individuals from each cluster (from the evaluated
        /// population) into the offspring pool, replacing the tail. This
        /// guarantees every cluster is represented in the next generation.
        /// </summary>
        private static List<GAIndividual> InjectClusterElites(
            List<GAIndividual> offspring,
            List<GAIndividual> evaluated,
            int numClusters,
            int elitesPerCluster)
        {
            var clusterElites = new List<GAIndividual>();
            for (int c = 0; c < numClusters; c++)
            {
                var best = evaluated
                    .Where(i => i.ClustGrp == c)
                    .OrderBy(i => i.Fitness)
                    .Take(elitesPerCluster)
                    .Select(i => i.Clone())
                    .ToList();
                clusterElites.AddRange(best);
            }

            if (clusterElites.Count == 0)
                return offspring;

            int targetSize = offspring.Count;
            var eliteIds = new HashSet<string>(clusterElites.Select(e => e.Id));
            var filtered = offspring.Where(o => !eliteIds.Contains(o.Id)).ToList();

            var result = new List<GAIndividual>(clusterElites);
            result.AddRange(filtered);

            if (result.Count > targetSize)
                result = result.Take(targetSize).ToList();

            return result;
        }

        private static List<SG_Rule> EnsureInitShapeFirst(List<SG_Rule> rules)
        {
            var initRules = rules.Where(r => r is SG_AutoRule_InitShape_3D).ToList();
            if (initRules.Count == 0) return rules;
            var otherRules = rules.Where(r => !(r is SG_AutoRule_InitShape_3D)).ToList();
            var sorted = new List<SG_Rule>(initRules);
            sorted.AddRange(otherRules);
            return sorted;
        }

        private List<int> GetChromosomeLengths(List<SG_Rule> rules, SG_Shape inputShape)
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
        /// Evaluates a population of individuals.
        /// Feasibility is computed on the graph (cheap) before expensive FEM.
        ///
        /// Performance-critical method. Two flags control how much memory we burn per individual:
        ///   • <paramref name="keepFullGeometry"/> = true → store the live shape + model reference for
        ///     every successful individual (used for first and last generation).
        ///   • <paramref name="keepTopPerCluster"/> = true → still store live shape/model references
        ///     but expect the caller to deep-copy only the top-1 per cluster afterwards.
        ///
        /// In both cases we hold REFERENCES (no deep copy in the inner loop) — the caller decides what
        /// to keep and deep-copies only those. This removes the dominant per-individual deep-copy cost
        /// (≈ 99% of evaluated individuals were being deep-copied twice and then thrown away).
        ///
        /// Per-stage timings are accumulated across the WHOLE run into <see cref="_stageTimingsMs"/>
        /// (not just gen 0).  Stopwatch overhead is on the order of microseconds — negligible
        /// compared to FEM/Mesh.ShrinkWrap calls — and the cumulative profile is what actually
        /// reveals the bottleneck.
        /// </summary>
        private List<GAIndividual> EvaluatePopulation(
            List<GAIndividual> population, SG_Shape iniShape, List<SG_Rule> rules,
            bool keepFullGeometry, bool keepTopPerCluster,
            out List<SG_Shape> shapesOut, out List<TB_Model> modelsOut)
        {
            int n = population?.Count ?? 0;
            if (n == 0)
                throw new InvalidOperationException("Population not initialized");

            // We hold REFERENCES here, not deep copies. Deep copy happens only for the
            // few individuals the caller decides to keep (top-per-cluster + first/last gen).
            shapesOut = new List<SG_Shape>(n);
            modelsOut = new List<TB_Model>(n);
            var evaluatedPop = new List<GAIndividual>(n);

            int failCount = 0;
            string firstError = null;

            // Per-stage timing always on — cumulative across all generations. Stopwatch overhead
            // (~100 ns per Start/Stop pair) is negligible vs. the work being timed (FEM, ShrinkWrap…).
            const bool timingEnabled = true;
            var swStage = new Stopwatch();
            var swMetric = new Stopwatch();

            // Pre-compute the per-metric timing keys (avoid string concatenation in the hot loop).
            var topoKeys = _topoMetricTypes
                .Select(mt => "Topo[" + TopologyMetrics.GetLabel(mt) + "]")
                .ToArray();
            var shapeKeys = _shapeMetricTypes
                .Select(mt => "Shape[" + ShapeMetrics.GetLabel(mt) + "]")
                .ToArray();

            for (int i = 0; i < n; i++)
            {
                GAIndividual individual = population[i];

                try
                {
                    SG_Genotype gt = CreateGenotypeFromIndividual(individual);

                    if (timingEnabled) swStage.Restart();
                    SG_Shape shape = CloneShape(iniShape);
                    if (timingEnabled) AccumStage("CloneIniShape", swStage);

                    if (timingEnabled) swStage.Restart();
                    var ruleMessages = (timingEnabled && i == 0) ? new List<string>() : null;
                    for (int j = 0; j < rules.Count; j++)
                    {
                        string message = rules[j].RuleOperation(ref shape, ref gt);
                        ruleMessages?.Add(message);
                    }
                    if (timingEnabled) AccumStage("RuleOps", swStage);

                    if (timingEnabled) swStage.Restart();
                    shape.RegisterElemsToNodes();
                    if (timingEnabled) AccumStage("RegisterElems", swStage);

                    if (timingEnabled) swStage.Restart();
                    EnforceBoundaryConstraints(shape, rules);
                    if (timingEnabled) AccumStage("BoundaryEnforce", swStage);

                    if (timingEnabled) swStage.Restart();
                    RepairSupportsAndLoads(shape, rules);
                    if (timingEnabled) AccumStage("RepairSupportsLoads", swStage);

                    if (_useSelfWeight)
                    {
                        if (timingEnabled) swStage.Restart();
                        ApplySelfWeightLoads(shape, _gravityDir);
                        if (timingEnabled) AccumStage("SelfWeight", swStage);
                    }

                    if (_currentGeneration == 0 && i == 0)
                    {
                        var diagParts = new List<string>();
                        diagParts.Add("=== Individual #0 diagnostics ===");
                        if (ruleMessages != null)
                            for (int ri = 0; ri < ruleMessages.Count; ri++)
                                diagParts.Add(string.Format("  Rule {0}: {1}", ri, ruleMessages[ri]));
                        diagParts.Add(string.Format("  Shape: {0} elems, {1} nodes, {2} supports, {3} loads",
                            shape.Elems?.Count ?? 0, shape.Nodes?.Count ?? 0,
                            shape.Supports?.Count ?? 0, shape.PointLoads?.Count ?? 0));
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            string.Join("\n", diagParts));
                    }

                    if (timingEnabled) swStage.Restart();
                    FeasibilityResult feasResult = FeasibilityMetrics.Compute(shape, _feasibilitySettings);
                    if (timingEnabled) AccumStage("Feasibility", swStage);

                    if (timingEnabled) swStage.Restart();
                    TB_Model tb_mdl = new TB_Model(shape);
                    SolveLS slv = new SolveLS(ref tb_mdl);
                    TB_Model finalModel = slv.Mdl;
                    if (timingEnabled) AccumStage("FEM_Solve", swStage);

                    if (_croSecOptMode != 0)
                    {
                        if (timingEnabled) swStage.Restart();
                        if (_croSecOptMode == 1)
                            finalModel = OptimizeCrossSections(finalModel);
                        else if (_croSecOptMode == 2)
                            finalModel = OptimizeCrossSections_SHS(finalModel);
                        else if (_croSecOptMode == 3)
                            finalModel = OptimizeCrossSections_Combined(finalModel, heaOnly: true, includeRHS: false);
                        else if (_croSecOptMode == 4)
                            finalModel = OptimizeCrossSections_Combined(finalModel, heaOnly: false, includeRHS: false);
                        else if (_croSecOptMode == 5)
                            finalModel = OptimizeCrossSections_RHS(finalModel);
                        else if (_croSecOptMode == 6)
                            finalModel = OptimizeCrossSections_Combined(finalModel, heaOnly: false, includeRHS: true);
                        if (timingEnabled) AccumStage("CroSecOpt", swStage);
                    }

                    double rawDisp = CalculateMaxNodalDisplacement(finalModel);

                    // Per-metric timing: each topology and shape metric is timed individually so
                    // the user can see exactly which one (e.g. ShrinkWrapVolume) dominates the run.
                    var topoVals = new List<double>(_topoMetricTypes.Count);
                    for (int mi = 0; mi < _topoMetricTypes.Count; mi++)
                    {
                        swMetric.Restart();
                        topoVals.Add(TopologyMetrics.Compute(shape, _topoMetricTypes[mi]));
                        AccumStage(topoKeys[mi], swMetric);
                    }

                    var shpeVals = new List<double>(_shapeMetricTypes.Count);
                    for (int mi = 0; mi < _shapeMetricTypes.Count; mi++)
                    {
                        swMetric.Restart();
                        // fastMode = true: ShrinkWrapVolume runs with coarse mesh parameters
                        // (≥0.1 detail ratio, no smoothing/poly-opt, capped beam sampling) so the
                        // metric scales to 1000×100 evaluations. Other metrics ignore the flag.
                        shpeVals.Add(ShapeMetrics.Compute(shape, _shapeMetricTypes[mi],
                            _shapeShrinkWrapDetailRatio, fastMode: true));
                        AccumStage(shapeKeys[mi], swMetric);
                    }

                    double spanL = ComputeSpanL(finalModel);
                    double slsLimit = spanL / 300.0;
                    double dispRatio = (slsLimit > 1e-12) ? rawDisp / slsLimit : double.MaxValue;

                    if (_currentGeneration == 0 && i == 0)
                    {
                        int tbSupCount = finalModel?.Nodes?.Count(n2 => n2.Sup != null) ?? 0;
                        int tbFreeCount = finalModel?.Nodes?.Count(n2 => n2.Sup == null) ?? 0;
                        int hasDisps = finalModel?.Nodes?.Count(n2 => n2.Disps != null && n2.Disps.Count > 0) ?? 0;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            string.Format("  FEM: TB_Nodes={0} (sup={1}, free={2}), hasDisps={3}, rawDisp={4:E3}, spanL={5:F1}, dispRatio={6:E3}",
                                finalModel?.Nodes?.Count ?? 0, tbSupCount, tbFreeCount,
                                hasDisps, rawDisp, spanL, dispRatio));
                    }

                    if (timingEnabled) swStage.Restart();
                    double avgUtil = ComputeAverageUtilization(finalModel);
                    const double TARGET_UTIL = 0.90;
                    double utilDev = Math.Abs(avgUtil - TARGET_UTIL);
                    if (avgUtil > 1.0)
                        utilDev = (avgUtil - TARGET_UTIL) * 2.0;
                    double maxUtil = ComputeMaxUtilization(finalModel);
                    if (timingEnabled) AccumStage("Utilization", swStage);

                    double rawFeas = (feasResult.VDang + feasResult.VAng + feasResult.VLen + feasResult.VBoundary) / 4.0;
                    rawFeas = Math.Clamp(rawFeas, 0.0, 1.0);

                    double utilObj = _utilObjType == 1 ? maxUtil : utilDev;

                    if (_numObjectives > 1)
                    {
                        double dispObj = Math.Log(1.0 + Math.Max(0.0, dispRatio));
                        individual.Fitness = dispRatio;
                        individual.ObjectiveValues = new List<double> { dispObj, utilObj };
                        if (_numObjectives >= 3)
                            individual.ObjectiveValues.Add(rawFeas);
                    }
                    else
                    {
                        double singleFitness;
                        switch (_singleObjType)
                        {
                            case 1: singleFitness = feasResult.TotalViolation; break;
                            case 2: singleFitness = utilDev; break;
                            case 3: singleFitness = maxUtil; break;
                            default: singleFitness = (rawDisp >= double.MaxValue || rawDisp <= double.MinValue)
                                ? rawDisp : rawDisp * (1.0 + feasResult.TotalViolation); break;
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

                    // Memory-critical: hold REFERENCES, not deep copies. Caller deep-copies only kept items.
                    // For the cheap "neither full nor top-per-cluster" path we discard entirely.
                    evaluatedPop.Add(individual);
                    if (keepFullGeometry || keepTopPerCluster)
                    {
                        shapesOut.Add(shape);
                        modelsOut.Add(finalModel);
                    }
                    else
                    {
                        shapesOut.Add(null);
                        modelsOut.Add(null);
                    }
                }
                catch (Exception ex)
                {
                    individual.Fitness = MAXIMIZE ? double.MinValue : double.MaxValue;
                    individual.TopoValues = _topoMetricTypes.Select(_ => 0.0).ToList();
                    individual.ShpeValues = _shapeMetricTypes.Select(_ => 0.0).ToList();
                    individual.Feas = 0;
                    individual.VDang = 0;

                    if (_numObjectives > 1)
                    {
                        individual.ObjectiveValues = new List<double> { double.MaxValue, double.MaxValue };
                        if (_numObjectives >= 3)
                            individual.ObjectiveValues.Add(double.MaxValue);
                    }

                    evaluatedPop.Add(individual);
                    shapesOut.Add(null);
                    modelsOut.Add(null);

                    failCount++;
                    if (firstError == null) firstError = ex.ToString();
                }
            }

            // Two-pass deep copy: only for the items the caller actually wants to keep alive.
            // Full-geometry generations (first + last) → deep copy every successful individual.
            // Top-per-cluster generations → caller picks the top-1 per cluster and deep copies those.
            if (keepFullGeometry)
            {
                for (int i = 0; i < shapesOut.Count; i++)
                {
                    if (shapesOut[i] != null) shapesOut[i] = UT.DeepCopy(shapesOut[i]);
                    if (modelsOut[i] != null) modelsOut[i] = CloneModel(modelsOut[i]);
                }
            }
            // For keepTopPerCluster the deep copy happens inside AppendTopPerCluster, so we leave references in place.

            int successCount = n - failCount;
            if (failCount > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    string.Format("Gen {0}: {1}/{2} succeeded, {3} failed. First error: {4}",
                        _currentGeneration, successCount, n, failCount, firstError));
            }

            return evaluatedPop;
        }

        private void AccumStage(string key, Stopwatch sw)
        {
            sw.Stop();
            if (_stageTimingsMs == null) return;
            double ms = sw.Elapsed.TotalMilliseconds;
            if (_stageTimingsMs.TryGetValue(key, out double existing))
                _stageTimingsMs[key] = existing + ms;
            else
                _stageTimingsMs[key] = ms;
        }

        /// <summary>
        /// Creates a genotype from a GA individual.
        /// </summary>
        private SG_Genotype CreateGenotypeFromIndividual(GAIndividual individual)
        {
            List<int> intGenes = new List<int>(individual.Chromosome);
            List<double> dGenes = new List<double>(individual.ChromosomeParam);
            SG_Genotype gt = new SG_Genotype(intGenes, dGenes);
            return gt;
        }

        /// <summary>
        /// Calculates the maximum nodal displacement from the analysis results.
        /// </summary>
        private double CalculateMaxNodalDisplacement(TB_Model model)
        {
            double maxDisplacement = 0.0;

            if (model == null || model.Nodes == null || model.Nodes.Count == 0)
            {
                return MAXIMIZE ? double.MinValue : double.MaxValue;
            }

            foreach (var node in model.Nodes)
            {
                if (node.Disps != null && node.Disps.Count > 0)
                {
                    double[] disp = node.Disps.Last();

                    if (disp != null && disp.Length >= 3)
                    {
                        double displacement = Math.Sqrt(
                            disp[0] * disp[0] +
                            disp[1] * disp[1] +
                            disp[2] * disp[2]
                        );

                        if (displacement > maxDisplacement)
                        {
                            maxDisplacement = displacement;
                        }
                    }
                }
            }

            if (maxDisplacement == 0.0)
            {
                return MAXIMIZE ? double.MinValue : double.MaxValue;
            }

            return maxDisplacement;
        }

        /// <summary>
        /// Computes the reference span L as the maximum distance between any two
        /// supported nodes. Falls back to the max distance from any support to
        /// any node if only one support exists.
        /// </summary>
        private static double ComputeSpanL(TB_Model model)
        {
            if (model?.Sups == null || model.Sups.Count == 0)
                return 1.0;

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

        /// <summary>
        /// Computes the average element utilization (EC3 simplified: N/N_Rd + My/My_Rd + Mz/Mz_Rd).
        /// Returns a value where 1.0 = 100 % utilized on average.
        /// </summary>
        private static double ComputeAverageUtilization(TB_Model model)
        {
            if (model?.Elem1Ds == null || model.Elem1Ds.Count == 0)
                return 0.0;

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

        /// <summary>
        /// Computes the maximum element utilization. Used for "minimize highest utilization" objective.
        /// </summary>
        private static double ComputeMaxUtilization(TB_Model model)
        {
            if (model?.Elem1Ds == null || model.Elem1Ds.Count == 0)
                return 0.0;

            double maxU = 0.0;
            foreach (var elem in model.Elem1Ds)
            {
                double u = ComputeElementUtilization(model, elem);
                if (u < double.MaxValue && u > maxU)
                    maxU = u;
            }
            return maxU;
        }

        /// <summary>
        /// Adds self-weight as lumped nodal point loads.
        /// Each element's weight is split 50/50 to its two end nodes, applied along the gravity direction.
        /// Weight [kN] = length [m] × (Area [mm²] × 1e-6) [m²] × Density [kN/m³].
        /// </summary>
        private void ApplySelfWeightLoads(SG_Shape shape, Vector3d gravityDir)
        {
            if (shape == null || shape.Nodes == null || shape.Elems == null)
                return;

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

            if (shape.PointLoads == null)
                shape.PointLoads = new List<SG_PointLoad>();

            foreach (var node in shape.Nodes)
            {
                double force = nodalForces.ContainsKey(node.ID) ? nodalForces[node.ID] : 0.0;
                if (force <= 0) continue;

                shape.PointLoads.Add(new SG_PointLoad(
                    gravityDir * force,
                    Vector3d.Zero,
                    node.Pt));
            }
        }

        /// <summary>
        /// Filters data by top pctTop% per cluster (by fitness, ascending for minimize).
        /// </summary>
        private static List<(SG_Shape Shape, int Cluster, double Fitness)> FilterTopPercent(
            List<(SG_Shape Shape, int Cluster, double Fitness)> data, double pctTop, bool minimize = true)
        {
            if (data == null || data.Count == 0) return new List<(SG_Shape, int, double)>();
            if (pctTop >= 100.0) return data;
            var result = new List<(SG_Shape, int, double)>();
            var byCluster = data.GroupBy(x => x.Cluster);
            foreach (var grp in byCluster)
            {
                int take = Math.Max(1, (int)Math.Ceiling(grp.Count() * pctTop / 100.0));
                var ordered = minimize ? grp.OrderBy(x => x.Fitness) : grp.OrderByDescending(x => x.Fitness);
                result.AddRange(ordered.Take(take));
            }
            return result;
        }

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0.0, 1.0);
            int g = t <= 0.5 ? (int)(t / 0.5 * 255) : 255;
            int b = t <= 0.5 ? 255 : (int)((1.0 - (t - 0.5) / 0.5) * 255);
            return Color.FromArgb(0, Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        /// <summary>
        /// Pushes results to all 15 output sockets: first/last generation shapes (filtered by %),
        /// per-cluster colours, the top-1-per-cluster-per-gen tree, the convergence trees + graph,
        /// the CSV / JSON paths, and the lightweight assembly.
        /// </summary>
        private void OutputFirstLastAndConvergence(IGH_DataAccess DA, string lastClusterLog,
            double pctTop, Point3d insertPt, double graphW, double graphH, SGShapeGrammar3DAssembly assembly,
            string csvPath, string jsonPath, double totalSeconds)
        {
            var rawFirst = _firstGenData ?? new List<(SG_Shape, int, double)>();
            var rawLast = _lastGenData ?? new List<(SG_Shape, int, double)>();

            var firstFiltered = FilterTopPercent(rawFirst, pctTop, !MAXIMIZE);
            var lastFiltered = FilterTopPercent(rawLast, pctTop, !MAXIMIZE);

            var firstShapes = firstFiltered.Select(x => x.Shape).ToList();
            var lastShapes = lastFiltered.Select(x => x.Shape).ToList();
            var firstColours = firstFiltered.Select(x => GetClusterColour(x.Cluster, _numClusters)).ToList();
            var lastColours = lastFiltered.Select(x => GetClusterColour(x.Cluster, _numClusters)).ToList();

            // 1 / 2: First and Last generation shape lists
            DA.SetDataList(1, firstShapes);
            DA.SetDataList(2, lastShapes);

            // 3 / 6: Top per Gen-Cluster tree (path = generation, items ordered by cluster id)
            var topShapesTree = new GH_Structure<IGH_Goo>();
            var topColoursTree = new GH_Structure<GH_Colour>();
            if (_topPerGenCluster != null)
            {
                foreach (var grp in _topPerGenCluster.GroupBy(t => t.Generation).OrderBy(g => g.Key))
                {
                    var path = new GH_Path(grp.Key);
                    foreach (var (_, cluster, shape, _, _, _) in grp.OrderBy(x => x.Cluster))
                    {
                        topShapesTree.Append(new GH_ObjectWrapper(shape), path);
                        topColoursTree.Append(new GH_Colour(GetClusterColour(cluster, _numClusters)), path);
                    }
                }
            }
            DA.SetDataTree(3, topShapesTree);

            // 4 / 5: First / Last cluster colours
            DA.SetDataList(4, firstColours);
            DA.SetDataList(5, lastColours);

            DA.SetDataTree(6, topColoursTree);

            // 7 / 8 / 9: convergence number trees
            var convBest = new GH_Structure<GH_Number>();
            var convWorst = new GH_Structure<GH_Number>();
            var convAvg = new GH_Structure<GH_Number>();
            if (_convergenceData != null)
            {
                foreach (var (gen, cluster, best, worst, avg) in _convergenceData)
                {
                    var path = new GH_Path(gen, cluster);
                    convBest.Append(new GH_Number(best), path);
                    convWorst.Append(new GH_Number(worst), path);
                    convAvg.Append(new GH_Number(avg), path);
                }
            }
            DA.SetDataTree(7, convBest);
            DA.SetDataTree(8, convWorst);
            DA.SetDataTree(9, convAvg);

            // 10 / 11: convergence graph lines + colours
            var linesTree = new GH_Structure<GH_Line>();
            var colorsTree = new GH_Structure<GH_Colour>();
            if (_convergenceData != null && _convergenceData.Count > 0 && _numClusters > 0)
                BuildConvergenceGraph(insertPt, graphW, graphH, linesTree, colorsTree);
            DA.SetDataTree(10, linesTree);
            DA.SetDataTree(11, colorsTree);

            // 12 / 13: streaming output paths (so the user can wire a Panel to see where the run was saved)
            DA.SetData(12, csvPath ?? string.Empty);
            DA.SetData(13, jsonPath ?? string.Empty);

            // 14: lightweight assembly
            DA.SetData(14, new GH_SGAssembly(assembly ?? new SGShapeGrammar3DAssembly()));

            // 0: Info text — adds per-stage timing breakdown for gen 0 to expose the bottleneck
            string timingBlock = BuildTimingBlock();
            string info = string.Format(
                "GI_Large run {0}: {1} pop × {2} gen × {3} clusters in {4:F1}s\n" +
                "  First gen: {5} shapes (top {6:F0}% per cluster), Last gen: {7} shapes\n" +
                "  Top per cluster per gen records: {8}, Streamed individuals: {9}\n" +
                "  CSV : {10}\n" +
                "  JSON: {11}\n\n" +
                "{12}\n" +
                "{13}",
                _runStore?.RunId ?? "?",
                _populationSize, _numGenerations, _numClusters, totalSeconds,
                firstShapes.Count, pctTop, lastShapes.Count,
                _topPerGenCluster?.Count ?? 0,
                _runStore?.TotalIndividualsRecorded ?? 0,
                csvPath ?? "(none)", jsonPath ?? "(none)",
                timingBlock,
                lastClusterLog);
            DA.SetData(0, info);
        }

        /// <summary>
        /// Builds a per-stage and per-metric timing breakdown across the WHOLE run so the user
        /// can immediately see which stage (RuleOps / Boundary / FEM / CroSecOpt …) and which
        /// individual metric (e.g. Shape[ShrinkWrap Volume]) is the bottleneck.
        /// Stages are sorted descending by total wall-clock time so the costliest item is first.
        /// </summary>
        private string BuildTimingBlock()
        {
            if (_stageTimingsMs == null || _stageTimingsMs.Count == 0)
                return string.Empty;
            double total = _stageTimingsMs.Values.Sum();
            if (total <= 0.0) return string.Empty;

            int totalEvals = Math.Max(1, _populationSize) * Math.Max(1, _numGenerations);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(
                "Run-wide per-stage timings ({0:F0} ms total over {1} individuals × {2} generations = {3} evals):",
                total, _populationSize, _numGenerations, totalEvals));
            foreach (var kv in _stageTimingsMs.OrderByDescending(k => k.Value))
            {
                double pct = kv.Value / total * 100.0;
                double perEvalUs = kv.Value * 1000.0 / totalEvals; // ms -> µs per eval
                sb.AppendLine(string.Format("  {0,-32} {1,10:F1} ms  ({2,5:F1} %)  {3,8:F1} µs/eval",
                    kv.Key, kv.Value, pct, perEvalUs));
            }
            return sb.ToString();
        }

        private void BuildConvergenceGraph(Point3d insertPt, double graphW, double graphH,
            GH_Structure<GH_Line> linesTree, GH_Structure<GH_Colour> colorsTree)
        {
            Plane plane = new Plane(insertPt, Vector3d.XAxis, Vector3d.YAxis);
            var sortedGens = _convergenceData.Select(x => x.gen).Distinct().OrderBy(g => g).ToList();
            int numGens = sortedGens.Count;
            if (numGens < 2) return;

            double subGraphH = (graphH - (_numClusters - 1) * CONV_TEXT_HEIGHT * 2) / Math.Max(1, _numClusters);
            subGraphH = Math.Max(1.0, subGraphH);
            Color mainColor = Color.Black;
            Color gridColor = Color.FromArgb(180, 180, 180);
            Color bestColor = Color.FromArgb(40, 120, 40);
            Color worstColor = Color.FromArgb(180, 60, 60);
            Color avgColor = Color.FromArgb(100, 100, 100);

            for (int ci = 0; ci < _numClusters; ci++)
            {
                var clusterData = _convergenceData.Where(x => x.cluster == ci).OrderBy(x => x.gen).ToList();
                if (clusterData.Count == 0) continue;

                double yMin = double.MaxValue, yMax = double.MinValue;
                foreach (var d in clusterData)
                {
                    if (!double.IsNaN(d.best) && d.best < yMin) yMin = d.best;
                    if (!double.IsNaN(d.best) && d.best > yMax) yMax = d.best;
                    if (!double.IsNaN(d.worst) && d.worst < yMin) yMin = d.worst;
                    if (!double.IsNaN(d.worst) && d.worst > yMax) yMax = d.worst;
                    if (!double.IsNaN(d.avg) && d.avg < yMin) yMin = d.avg;
                    if (!double.IsNaN(d.avg) && d.avg > yMax) yMax = d.avg;
                }
                if (yMin >= yMax) yMax = yMin + 1.0;
                double yRange = yMax - yMin;
                double yPad = yRange * 0.05;
                yMin -= yPad;
                yMax += yPad;
                yRange = yMax - yMin;

                double yOffset = ci * (subGraphH + CONV_TEXT_HEIGHT * 2);
                Point3d origin = plane.Origin + plane.YAxis * (graphH - yOffset - subGraphH);

                GH_Path axisPath = new GH_Path(ci, 0);
                linesTree.Append(new GH_Line(new Line(origin, origin + plane.XAxis * graphW)), axisPath);
                colorsTree.Append(new GH_Colour(mainColor), axisPath);
                linesTree.Append(new GH_Line(new Line(origin, origin + plane.YAxis * subGraphH)), axisPath);
                colorsTree.Append(new GH_Colour(mainColor), axisPath);

                for (int gi = 0; gi < numGens; gi++)
                {
                    double xFrac = (double)gi / (numGens - 1);
                    Point3d tickBase = origin + plane.XAxis * (xFrac * graphW);
                    Point3d tickEnd = tickBase - plane.YAxis * (CONV_TEXT_HEIGHT * 0.3);
                    linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                    colorsTree.Append(new GH_Colour(mainColor), axisPath);
                    if (gi > 0)
                    {
                        linesTree.Append(new GH_Line(new Line(tickBase, tickBase + plane.YAxis * subGraphH)), axisPath);
                        colorsTree.Append(new GH_Colour(gridColor), axisPath);
                    }
                }

                for (int t = 0; t <= 4; t++)
                {
                    double frac = (double)t / 4;
                    Point3d tickBase = origin + plane.YAxis * (frac * subGraphH);
                    Point3d tickEnd = tickBase - plane.XAxis * (CONV_TEXT_HEIGHT * 0.3);
                    linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                    colorsTree.Append(new GH_Colour(mainColor), axisPath);
                }

                var bestPts = new List<Point3d>();
                var worstPts = new List<Point3d>();
                var avgPts = new List<Point3d>();
                for (int gi = 0; gi < numGens; gi++)
                {
                    int gen = sortedGens[gi];
                    var d = clusterData.FirstOrDefault(x => x.gen == gen);
                    double xFrac = (double)gi / (numGens - 1);
                    if (!double.IsNaN(d.best))
                    {
                        double yFrac = (d.best - yMin) / yRange;
                        yFrac = Math.Clamp(yFrac, 0, 1);
                        bestPts.Add(origin + plane.XAxis * (xFrac * graphW) + plane.YAxis * (yFrac * subGraphH));
                    }
                    if (!double.IsNaN(d.worst))
                    {
                        double yFrac = (d.worst - yMin) / yRange;
                        yFrac = Math.Clamp(yFrac, 0, 1);
                        worstPts.Add(origin + plane.XAxis * (xFrac * graphW) + plane.YAxis * (yFrac * subGraphH));
                    }
                    if (!double.IsNaN(d.avg))
                    {
                        double yFrac = (d.avg - yMin) / yRange;
                        yFrac = Math.Clamp(yFrac, 0, 1);
                        avgPts.Add(origin + plane.XAxis * (xFrac * graphW) + plane.YAxis * (yFrac * subGraphH));
                    }
                }
                GH_Path bestPath = new GH_Path(ci, 1);
                GH_Path worstPath = new GH_Path(ci, 2);
                GH_Path avgPath = new GH_Path(ci, 3);
                for (int i = 1; i < bestPts.Count; i++)
                {
                    linesTree.Append(new GH_Line(new Line(bestPts[i - 1], bestPts[i])), bestPath);
                    colorsTree.Append(new GH_Colour(bestColor), bestPath);
                }
                for (int i = 1; i < worstPts.Count; i++)
                {
                    linesTree.Append(new GH_Line(new Line(worstPts[i - 1], worstPts[i])), worstPath);
                    colorsTree.Append(new GH_Colour(worstColor), worstPath);
                }
                for (int i = 1; i < avgPts.Count; i++)
                {
                    linesTree.Append(new GH_Line(new Line(avgPts[i - 1], avgPts[i])), avgPath);
                    colorsTree.Append(new GH_Colour(avgColor), avgPath);
                }
            }
        }

        private void RecreateShapeAndModel(GAIndividual individual, SG_Shape iniShape, List<SG_Rule> rules, out SG_Shape shape, out TB_Model model)
        {
            SG_Genotype gt = CreateGenotypeFromIndividual(individual);

            shape = new SG_Shape
            {
                nodeCount = iniShape.nodeCount,
                elementCount = iniShape.elementCount,
                Elems = iniShape.Elems.Select(e => e.DeepClone()).ToList(),
                Nodes = iniShape.Nodes.Select(n => n.DeepClone()).ToList(),
                Supports = iniShape.Supports.Select(s => s.DeepClone()).ToList(),
                LineLoads = iniShape.LineLoads.Select(ll => (SG_LineLoad)ll.DeepClone()).ToList(),
                PointLoads = iniShape.PointLoads.Select(pl => (SG_PointLoad)pl.DeepClone()).ToList(),
                SimpleShapeState = iniShape.SimpleShapeState
            };

            for (int j = 0; j < rules.Count; j++)
            {
                string message = rules[j].RuleOperation(ref shape, ref gt);
            }

            shape.RegisterElemsToNodes();

            EnforceBoundaryConstraints(shape, rules);

            RepairSupportsAndLoads(shape, rules);

            if (_useSelfWeight)
                ApplySelfWeightLoads(shape, _gravityDir);

            model = new TB_Model(shape);
            SolveLS slv = new SolveLS(ref model);

            if (_croSecOptMode == 1)
                model = OptimizeCrossSections(model);
            else if (_croSecOptMode == 2)
                model = OptimizeCrossSections_SHS(model);
            else if (_croSecOptMode == 3)
                model = OptimizeCrossSections_Combined(model, heaOnly: true);
            else if (_croSecOptMode == 4)
                model = OptimizeCrossSections_Combined(model, heaOnly: false);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_Generic; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("A7B8C9D0-E1F2-4A3B-8C5D-6E7F8A9B0C1D"); }
        }

        /// <summary>
        /// Precomputed section properties for fast utilization evaluation
        /// during force-based section sizing (avoids creating Section objects).
        /// </summary>
        private struct PrecomputedSec
        {
            public double Area; // mm²
            public double Wy;   // mm³
            public double Wz;   // mm³
            public double Iy;   // mm⁴ (strong axis, needed for buckling)
            public double Iz;   // mm⁴ (weak axis, needed for buckling)
        }

        /// <summary>
        /// Extracts the maximum absolute internal forces (N, My, Mz)
        /// plus the most compressive axial force Ncomp (negative = compression),
        /// as an envelope over all load cases for one element.
        /// </summary>
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
                maxN  = Math.Max(maxN,  Math.Max(Math.Abs(F[0]),  Math.Abs(F[6])));
                maxMy = Math.Max(maxMy, Math.Max(Math.Abs(F[4]),  Math.Abs(F[10])));
                maxMz = Math.Max(maxMz, Math.Max(Math.Abs(F[5]),  Math.Abs(F[11])));
                double nComp = Math.Min(F[0], F[6]);
                if (nComp < maxNcomp) maxNcomp = nComp;
            }
            return (maxN, maxMy, maxMz, maxNcomp);
        }

        /// <summary>
        /// Computes utilization for given internal forces and precomputed section
        /// properties, INCLUDING a simplified EC3 buckling check for compression.
        /// </summary>
        private static double ComputeUtilForSection(
            (double N, double My, double Mz, double Ncomp) forces,
            PrecomputedSec sec, double fy, double E, double elemLengthM)
        {
            double nRd  = sec.Area * fy;
            double myRd = sec.Wy * fy * 1e-3;
            double mzRd = sec.Wz * fy * 1e-3;
            if (nRd <= 0 || myRd <= 0 || mzRd <= 0)
                return double.MaxValue;

            double utilCS = forces.N / nRd + forces.My / myRd + forces.Mz / mzRd;

            if (forces.Ncomp >= 0)
                return utilCS;

            double Lcr = elemLengthM * 1000.0;
            double alphaBuck = fy < 460 ? 0.21 : 0.13;
            double lambda1 = Math.PI * Math.Sqrt(E / fy);

            double chi = ComputeChiMin(sec.Area, sec.Iy, sec.Iz, Lcr, alphaBuck, lambda1);

            double N_Ed = Math.Abs(forces.Ncomp);
            double N_bRd = chi * sec.Area * fy;
            double utilBuckling = N_Ed / N_bRd + forces.My / myRd + forces.Mz / mzRd;

            return Math.Max(utilCS, utilBuckling);
        }

        /// <summary>
        /// Computes the minimum buckling reduction factor Chi across both axes.
        /// </summary>
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

        /// <summary>
        /// Linear scan of the catalog to find the section whose utilization is
        /// closest to targetUtil without exceeding 1.0.
        /// Unlike binary search, this handles non-monotonic utilization sequences
        /// (which occur when the catalog is sorted by area but bending capacity
        /// doesn't increase monotonically with area).
        /// </summary>
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
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>
        /// Fully-Stressed-Design (FSD) iteration for rectangular sections.
        /// Includes simplified EC3 buckling check for compression members.
        /// </summary>
        private TB_Model OptimizeCrossSections(TB_Model solvedModel)
        {
            if (solvedModel == null || solvedModel.Elem1Ds == null || solvedModel.Elem1Ds.Count == 0)
                return solvedModel;

            const int NUM_SIZES = 20;
            const double STEP_MM = 50.0;
            const double TARGET_UTIL = 0.90;
            int maxIter = _croSecMaxIter;

            var catalog = new PrecomputedSec[NUM_SIZES];
            for (int i = 0; i < NUM_SIZES; i++)
            {
                double dim = (i + 1) * STEP_MM;
                double inertia = Math.Pow(dim, 4) / 12.0;
                catalog[i] = new PrecomputedSec
                {
                    Area = dim * dim,
                    Wy   = dim * dim * dim / 6.0,
                    Wz   = dim * dim * dim / 6.0,
                    Iy   = inertia,
                    Iz   = inertia
                };
            }

            int elemCount = solvedModel.Elem1Ds.Count;
            double[] elemLengths = solvedModel.Elem1Ds.Select(e => e.Line.Length).ToArray();

            int[] secIdx = new int[elemCount];
            for (int e = 0; e < elemCount; e++)
            {
                double fy = solvedModel.Elem1Ds[e].Sec.Mat.Fy;
                double E  = solvedModel.Elem1Ds[e].Sec.Mat.E;
                var forces = GetElementMaxForces(solvedModel, solvedModel.Elem1Ds[e]);
                secIdx[e] = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], TARGET_UTIL);
            }

            for (int iter = 0; iter < maxIter; iter++)
            {
                TB_Model model = RebuildModelWithRectSections(solvedModel, secIdx, STEP_MM);
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;

                int[] newIdx = new int[elemCount];
                int totalDelta = 0;
                double alpha = 0.7 + 0.25 * Math.Min(1.0, (double)iter / 10.0);

                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                    double E  = model.Elem1Ds[e].Sec.Mat.E;
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
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;

                bool anyBumped = false;
                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double util = ComputeElementUtilization(model, model.Elem1Ds[e]);
                    if (util > 1.0)
                    {
                        double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                        double E  = model.Elem1Ds[e].Sec.Mat.E;
                        var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                        int target = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], 1.0);
                        if (target > secIdx[e])
                        { secIdx[e] = target; anyBumped = true; }
                        else if (secIdx[e] < NUM_SIZES - 1)
                        { secIdx[e]++; anyBumped = true; }
                    }
                }
                if (!anyBumped) break;
            }

            TB_Model finalModel = RebuildModelWithRectSections(solvedModel, secIdx, STEP_MM);
            SolveLS finalSlv = new SolveLS(ref finalModel);
            return finalSlv.Mdl;
        }

        /// <summary>
        /// Rebuilds the model geometry with new square rectangular sections based on the index array.
        /// </summary>
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

        /// <summary>
        /// EC3 utilization with simplified buckling check for compression members.
        /// Cross-section: N/N_Rd + My/My_Rd + Mz/Mz_Rd.
        /// Buckling:      N/(Chi*A*fy) + My/My_Rd + Mz/Mz_Rd.
        /// Returns the maximum across all load cases.
        /// </summary>
        private static double ComputeElementUtilization(TB_Model model, TB_Element_1D elem)
        {
            const double GAMMA_M0 = 1.0;
            double fy = elem.Sec.Mat.Fy;
            double E  = elem.Sec.Mat.E;

            double N_Rd  = elem.Sec.Area * fy / GAMMA_M0;
            double My_Rd = elem.Sec.Wy   * fy / GAMMA_M0 * 1e-3;
            double Mz_Rd = elem.Sec.Wz   * fy / GAMMA_M0 * 1e-3;

            if (N_Rd <= 0 || My_Rd <= 0 || Mz_Rd <= 0)
                return double.MaxValue;

            double iy = Math.Sqrt(elem.Sec.Iy / elem.Sec.Area);
            double iz = Math.Sqrt(elem.Sec.Iz / elem.Sec.Area);

            double Lcr = elem.Line.Length * 1000.0;
            double alphaBuck = fy < 460 ? 0.21 : 0.13;
            double lambda1 = Math.PI * Math.Sqrt(E / fy);

            double chiY = 1.0, chiZ = 1.0;
            if (iy > 0)
            {
                double lb_y = (Lcr / iy) / lambda1;
                double phi_y = 0.5 * (1 + alphaBuck * (lb_y - 0.2) + lb_y * lb_y);
                double d_y = phi_y * phi_y - lb_y * lb_y;
                chiY = d_y > 0 ? Math.Min(1.0 / (phi_y + Math.Sqrt(d_y)), 1.0) : 0.01;
                chiY = Math.Max(chiY, 0.01);
            }
            if (iz > 0)
            {
                double lb_z = (Lcr / iz) / lambda1;
                double phi_z = 0.5 * (1 + alphaBuck * (lb_z - 0.2) + lb_z * lb_z);
                double d_z = phi_z * phi_z - lb_z * lb_z;
                chiZ = d_z > 0 ? Math.Min(1.0 / (phi_z + Math.Sqrt(d_z)), 1.0) : 0.01;
                chiZ = Math.Max(chiZ, 0.01);
            }
            double chi = Math.Min(chiY, chiZ);

            double maxUtil = 0.0;
            if (model.LCs == null) return maxUtil;

            foreach (int lc in model.LCs)
            {
                int lcId = Array.IndexOf(model.LCs, lc);
                double[] F = elem.Calc_Forces(lcId);

                double N_Ed  = Math.Max(Math.Abs(F[0]),  Math.Abs(F[6]));
                double My_Ed = Math.Max(Math.Abs(F[4]),  Math.Abs(F[10]));
                double Mz_Ed = Math.Max(Math.Abs(F[5]),  Math.Abs(F[11]));

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

        /// <summary>
        /// Fully-Stressed-Design (FSD) iteration for SHS catalog sections.
        /// Includes simplified EC3 buckling check for compression members.
        /// Uses linear scan instead of binary search to handle non-monotonic
        /// utilization in the area-sorted catalog.
        /// </summary>
        private TB_Model OptimizeCrossSections_SHS(TB_Model solvedModel)
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

            var sortedCombos = sortedRaw
                .Select(x => (x.Size, x.T, x.Area))
                .ToList();

            var catalog = sortedRaw
                .Select(x => new PrecomputedSec { Area = x.Area, Wy = x.Wy, Wz = x.Wy, Iy = x.Iy, Iz = x.Iy })
                .ToArray();

            int numOptions = catalog.Length;
            int elemCount = solvedModel.Elem1Ds.Count;
            double[] elemLengths = solvedModel.Elem1Ds.Select(e => e.Line.Length).ToArray();

            const double TARGET_UTIL = 0.90;
            int maxIter = _croSecMaxIter;

            int[] secIdx = new int[elemCount];
            for (int e = 0; e < elemCount; e++)
            {
                double fy = solvedModel.Elem1Ds[e].Sec.Mat.Fy;
                double E  = solvedModel.Elem1Ds[e].Sec.Mat.E;
                var forces = GetElementMaxForces(solvedModel, solvedModel.Elem1Ds[e]);
                secIdx[e] = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], TARGET_UTIL);
            }

            for (int iter = 0; iter < maxIter; iter++)
            {
                TB_Model model = RebuildModelWithSHSSections(solvedModel, secIdx, sortedCombos);
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;

                int[] newIdx = new int[elemCount];
                int totalDelta = 0;
                double alpha = 0.7 + 0.25 * Math.Min(1.0, (double)iter / 10.0);

                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                    double E  = model.Elem1Ds[e].Sec.Mat.E;
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
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;

                bool anyBumped = false;
                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double util = ComputeElementUtilization(model, model.Elem1Ds[e]);
                    if (util > 1.0)
                    {
                        double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                        double E  = model.Elem1Ds[e].Sec.Mat.E;
                        var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                        int target = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], 1.0);
                        if (target > secIdx[e])
                        { secIdx[e] = target; anyBumped = true; }
                        else if (secIdx[e] < numOptions - 1)
                        { secIdx[e]++; anyBumped = true; }
                    }
                }
                if (!anyBumped) break;
            }

            TB_Model finalModel = RebuildModelWithSHSSections(solvedModel, secIdx, sortedCombos);
            SolveLS finalSlv = new SolveLS(ref finalModel);
            return finalSlv.Mdl;
        }

        private static TB_Model RebuildModelWithSHSSections(
            TB_Model template, int[] secIdx,
            List<(int Size, int T, double Area)> sortedCombos)
        {
            var newElems = new List<TB_Element_1D>();
            for (int i = 0; i < template.Elem1Ds.Count; i++)
            {
                TB_Element_1D orig = template.Elem1Ds[i];
                int idx = Math.Clamp(secIdx[i], 0, sortedCombos.Count - 1);
                var combo = sortedCombos[idx];
                double size = combo.Size;
                double t = combo.T;
                string tag = string.Format("SHS_{0}x{0}x{1}", size, t);
                Section_RHS sec = new Section_RHS(orig.Sec.Mat, tag, size, size, t, t);
                newElems.Add(new TB_Element_1D(orig.Line, orig.Tag, sec, orig.Vz, orig.Line.Length));
            }
            return new TB_Model(newElems, template.Sups, template.Loads);
        }

        /// <summary>
        /// FSD iteration for RHS catalog (mode 5). Sorted by area; each element picks best RHS.
        /// </summary>
        private TB_Model OptimizeCrossSections_RHS(TB_Model solvedModel)
        {
            if (solvedModel == null || solvedModel.Elem1Ds == null || solvedModel.Elem1Ds.Count == 0)
                return solvedModel;

            var combos = RHS_Catalog.AllCombinations();
            if (combos.Count == 0) return solvedModel;

            var sortedRaw = combos
                .Select(c =>
                {
                    double h = c.H, b = c.B, t = c.T;
                    double bi = b - 2.0 * t;
                    double hi = h - 2.0 * t;
                    double area = h * b - bi * hi;
                    double iy = (b * Math.Pow(h, 3) - bi * Math.Pow(hi, 3)) / 12.0;
                    double iz = (h * Math.Pow(b, 3) - (h - 2.0 * t) * Math.Pow(b - 2.0 * t, 3)) / 12.0;
                    double wy = iy / (0.5 * h);
                    double wz = iz / (0.5 * b);
                    return (c.H, c.B, c.T, Area: area, Wy: wy, Wz: wz, Iy: iy, Iz: iz);
                })
                .OrderBy(x => x.Area)
                .ToList();

            var sortedCombos = sortedRaw.Select(x => (x.H, x.B, x.T)).ToList();
            var catalog = sortedRaw
                .Select(x => new PrecomputedSec { Area = x.Area, Wy = x.Wy, Wz = x.Wz, Iy = x.Iy, Iz = x.Iz })
                .ToArray();

            int numOptions = catalog.Length;
            int elemCount = solvedModel.Elem1Ds.Count;
            double[] elemLengths = solvedModel.Elem1Ds.Select(e => e.Line.Length).ToArray();
            const double TARGET_UTIL = 0.90;
            int maxIter = _croSecMaxIter;

            int[] secIdx = new int[elemCount];
            for (int e = 0; e < elemCount; e++)
            {
                double fy = solvedModel.Elem1Ds[e].Sec.Mat.Fy;
                double E  = solvedModel.Elem1Ds[e].Sec.Mat.E;
                var forces = GetElementMaxForces(solvedModel, solvedModel.Elem1Ds[e]);
                secIdx[e] = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], TARGET_UTIL);
            }

            for (int iter = 0; iter < maxIter; iter++)
            {
                TB_Model model = RebuildModelWithRHSSections(solvedModel, secIdx, sortedCombos);
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;

                int[] newIdx = new int[elemCount];
                int totalDelta = 0;
                double alpha = 0.7 + 0.25 * Math.Min(1.0, (double)iter / 10.0);
                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                    double E  = model.Elem1Ds[e].Sec.Mat.E;
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
                TB_Model model = RebuildModelWithRHSSections(solvedModel, secIdx, sortedCombos);
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;
                bool anyBumped = false;
                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double util = ComputeElementUtilization(model, model.Elem1Ds[e]);
                    if (util > 1.0)
                    {
                        double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                        double E  = model.Elem1Ds[e].Sec.Mat.E;
                        var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                        int target = FindBestSectionIdx(forces, catalog, fy, E, elemLengths[e], 1.0);
                        if (target > secIdx[e]) { secIdx[e] = target; anyBumped = true; }
                        else if (secIdx[e] < numOptions - 1) { secIdx[e]++; anyBumped = true; }
                    }
                }
                if (!anyBumped) break;
            }

            return RebuildModelWithRHSSections(solvedModel, secIdx, sortedCombos);
        }

        private static TB_Model RebuildModelWithRHSSections(
            TB_Model template, int[] secIdx,
            List<(int H, int B, int T)> sortedCombos)
        {
            var newElems = new List<TB_Element_1D>();
            for (int i = 0; i < template.Elem1Ds.Count; i++)
            {
                TB_Element_1D orig = template.Elem1Ds[i];
                int idx = Math.Clamp(secIdx[i], 0, sortedCombos.Count - 1);
                var combo = sortedCombos[idx];
                string tag = string.Format("RHS_{0}x{1}x{2}", combo.H, combo.B, combo.T);
                Section_RHS sec = new Section_RHS(orig.Sec.Mat, tag, combo.H, combo.B, combo.T, combo.T);
                newElems.Add(new TB_Element_1D(orig.Line, orig.Tag, sec, orig.Vz, orig.Line.Length));
            }
            return new TB_Model(newElems, template.Sups, template.Loads);
        }

        /// <summary>
        /// Combined catalog entry that can represent either SHS or I-section.
        /// </summary>
        private struct CombinedEntry
        {
            public PrecomputedSec Props;
            public bool IsI;            // false = SHS/RHS, true = I-section
            public double H, W, Tw, Tf; // section dimensions (SHS: H=W=Size, Tw=Tf=T)
            public string Tag;
        }

        /// <summary>
        /// Builds a combined catalog from SHS + (optional RHS) + HEA/HEB, sorted by area.
        /// </summary>
        private static CombinedEntry[] BuildCombinedCatalog(bool includeRHS = false)
        {
            var entries = new List<CombinedEntry>();

            foreach (var c in SHS_Catalog.AllCombinations())
            {
                double s = c.Size, t = c.T;
                double bi = s - 2 * t;
                double area = s * s - bi * bi;
                double iy = (Math.Pow(s, 4) - Math.Pow(bi, 4)) / 12.0;
                double wy = 2.0 * iy / s;

                entries.Add(new CombinedEntry
                {
                    Props = new PrecomputedSec { Area = area, Wy = wy, Wz = wy, Iy = iy, Iz = iy },
                    IsI = false,
                    H = s, W = s, Tw = t, Tf = t,
                    Tag = string.Format("SHS_{0}x{0}x{1}", s, t)
                });
            }

            if (includeRHS)
            {
                foreach (var c in RHS_Catalog.AllCombinations())
                {
                    double h = c.H, b = c.B, t = c.T;
                    double bi = b - 2.0 * t;
                    double hi = h - 2.0 * t;
                    double area = h * b - bi * hi;
                    double iy = (b * Math.Pow(h, 3) - bi * Math.Pow(hi, 3)) / 12.0;
                    double iz = (h * Math.Pow(b, 3) - (h - 2.0 * t) * Math.Pow(b - 2.0 * t, 3)) / 12.0;
                    double wy = iy / (0.5 * h);
                    double wz = iz / (0.5 * b);
                    entries.Add(new CombinedEntry
                    {
                        Props = new PrecomputedSec { Area = area, Wy = wy, Wz = wz, Iy = iy, Iz = iz },
                        IsI = false,
                        H = h, W = b, Tw = t, Tf = t,
                        Tag = string.Format("RHS_{0}x{1}x{2}", (int)h, (int)b, (int)t)
                    });
                }
            }

            foreach (var p in HEA_Catalog.AllProfiles())
            {
                double area = p.W * p.H - (p.W - p.Tw) * (p.H - 2.0 * p.Tf);
                double iy = (p.W * Math.Pow(p.H, 3) - (p.W - p.Tw) * Math.Pow(p.H - 2.0 * p.Tf, 3)) / 12.0;
                double iz = (2.0 * p.Tf * Math.Pow(p.W, 3) + (p.H - 2.0 * p.Tf) * Math.Pow(p.Tw, 3)) / 12.0;
                double wy = iy / (0.5 * p.H);
                double wz = iz / (0.5 * p.W);

                entries.Add(new CombinedEntry
                {
                    Props = new PrecomputedSec { Area = area, Wy = wy, Wz = wz, Iy = iy, Iz = iz },
                    IsI = true,
                    H = p.H, W = p.W, Tw = p.Tw, Tf = p.Tf,
                    Tag = p.Name
                });
            }

            return entries.OrderBy(e => e.Props.Area).ToArray();
        }

        /// <summary>
        /// Builds a catalog from HEA/HEB profiles only, sorted by area.
        /// </summary>
        private static CombinedEntry[] BuildHEACatalog()
        {
            var entries = new List<CombinedEntry>();

            foreach (var p in HEA_Catalog.AllProfiles())
            {
                double area = p.W * p.H - (p.W - p.Tw) * (p.H - 2.0 * p.Tf);
                double iy = (p.W * Math.Pow(p.H, 3) - (p.W - p.Tw) * Math.Pow(p.H - 2.0 * p.Tf, 3)) / 12.0;
                double iz = (2.0 * p.Tf * Math.Pow(p.W, 3) + (p.H - 2.0 * p.Tf) * Math.Pow(p.Tw, 3)) / 12.0;
                double wy = iy / (0.5 * p.H);
                double wz = iz / (0.5 * p.W);

                entries.Add(new CombinedEntry
                {
                    Props = new PrecomputedSec { Area = area, Wy = wy, Wz = wz, Iy = iy, Iz = iz },
                    IsI = true,
                    H = p.H, W = p.W, Tw = p.Tw, Tf = p.Tf,
                    Tag = p.Name
                });
            }

            return entries.OrderBy(e => e.Props.Area).ToArray();
        }

        /// <summary>
        /// Rebuilds the model with sections from a CombinedEntry catalog.
        /// Automatically creates Section_RHS for SHS entries and Section_I
        /// for HEA/HEB entries.
        /// </summary>
        private static TB_Model RebuildModelWithCombinedSections(
            TB_Model template, int[] secIdx, CombinedEntry[] catalog)
        {
            var newElems = new List<TB_Element_1D>();
            for (int i = 0; i < template.Elem1Ds.Count; i++)
            {
                TB_Element_1D orig = template.Elem1Ds[i];
                int idx = Math.Clamp(secIdx[i], 0, catalog.Length - 1);
                CombinedEntry ce = catalog[idx];

                TB_Section sec;
                if (ce.IsI)
                    sec = new Section_I(orig.Sec.Mat, ce.Tag, ce.H, ce.W, ce.Tw, ce.Tf);
                else
                    sec = new Section_RHS(orig.Sec.Mat, ce.Tag, ce.H, ce.W, ce.Tw, ce.Tf);

                newElems.Add(new TB_Element_1D(orig.Line, orig.Tag, sec, orig.Vz, orig.Line.Length));
            }
            return new TB_Model(newElems, template.Sups, template.Loads);
        }

        /// <summary>
        /// FSD optimization using combined catalog (mode 3 = HEA only, 4 = SHS+HEA, 6 = SHS+RHS+HEA).
        /// </summary>
        private TB_Model OptimizeCrossSections_Combined(TB_Model solvedModel, bool heaOnly, bool includeRHS = false)
        {
            if (solvedModel == null || solvedModel.Elem1Ds == null || solvedModel.Elem1Ds.Count == 0)
                return solvedModel;

            CombinedEntry[] catalog = heaOnly ? BuildHEACatalog() : BuildCombinedCatalog(includeRHS);
            if (catalog.Length == 0) return solvedModel;

            PrecomputedSec[] props = catalog.Select(c => c.Props).ToArray();

            int numOptions = catalog.Length;
            int elemCount = solvedModel.Elem1Ds.Count;
            double[] elemLengths = solvedModel.Elem1Ds.Select(e => e.Line.Length).ToArray();

            const double TARGET_UTIL = 0.90;
            int maxIter = _croSecMaxIter;

            int[] secIdx = new int[elemCount];
            for (int e = 0; e < elemCount; e++)
            {
                double fy = solvedModel.Elem1Ds[e].Sec.Mat.Fy;
                double E  = solvedModel.Elem1Ds[e].Sec.Mat.E;
                var forces = GetElementMaxForces(solvedModel, solvedModel.Elem1Ds[e]);
                secIdx[e] = FindBestSectionIdx(forces, props, fy, E, elemLengths[e], TARGET_UTIL);
            }

            for (int iter = 0; iter < maxIter; iter++)
            {
                TB_Model model = RebuildModelWithCombinedSections(solvedModel, secIdx, catalog);
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;

                int[] newIdx = new int[elemCount];
                int totalDelta = 0;
                double alpha = 0.7 + 0.25 * Math.Min(1.0, (double)iter / 10.0);

                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                    double E  = model.Elem1Ds[e].Sec.Mat.E;
                    var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                    int target = FindBestSectionIdx(forces, props, fy, E, elemLengths[e], TARGET_UTIL);
                    int damped = (int)Math.Round(secIdx[e] + alpha * (target - secIdx[e]));
                    newIdx[e] = Math.Clamp(damped, 0, numOptions - 1);
                    totalDelta += Math.Abs(newIdx[e] - secIdx[e]);
                }

                secIdx = newIdx;
                if (totalDelta == 0) break;
            }

            for (int safety = 0; safety < 5; safety++)
            {
                TB_Model model = RebuildModelWithCombinedSections(solvedModel, secIdx, catalog);
                SolveLS slv = new SolveLS(ref model);
                model = slv.Mdl;

                bool anyBumped = false;
                for (int e = 0; e < model.Elem1Ds.Count; e++)
                {
                    double util = ComputeElementUtilization(model, model.Elem1Ds[e]);
                    if (util > 1.0)
                    {
                        double fy = model.Elem1Ds[e].Sec.Mat.Fy;
                        double E  = model.Elem1Ds[e].Sec.Mat.E;
                        var forces = GetElementMaxForces(model, model.Elem1Ds[e]);
                        int target = FindBestSectionIdx(forces, props, fy, E, elemLengths[e], 1.0);
                        if (target > secIdx[e])
                        { secIdx[e] = target; anyBumped = true; }
                        else if (secIdx[e] < numOptions - 1)
                        { secIdx[e]++; anyBumped = true; }
                    }
                }
                if (!anyBumped) break;
            }

            TB_Model finalModel = RebuildModelWithCombinedSections(solvedModel, secIdx, catalog);
            SolveLS finalSlv = new SolveLS(ref finalModel);
            return finalSlv.Mdl;
        }

        /// <summary>
        /// Writes optimized cross-sections from the solved model back to the shape
        /// so that stored assembly shapes (and GI Feasibility Preview) show correct column/beam sizes.
        /// </summary>
        private static void SyncShapeSectionsFromModel(SG_Shape shape, TB_Model model)
        {
            if (shape?.Elems == null || model?.Elem1Ds == null || shape.Elems.Count != model.Elem1Ds.Count)
                return;
            for (int i = 0; i < shape.Elems.Count; i++)
            {
                if (!(shape.Elems[i] is SG_Elem1D sgElem) || model.Elem1Ds[i]?.Sec == null)
                    continue;
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

            var elemEndpoints = new HashSet<int>();
            if (shape.Elems != null)
            {
                foreach (var e in shape.Elems)
                {
                    if (e?.Nodes == null) continue;
                    foreach (var n in e.Nodes)
                        if (n != null) elemEndpoints.Add(n.ID);
                }
            }

            foreach (var nd in shape.Nodes)
            {
                if (nd?.Support == null) continue;
                if (nd.Support.SupportCondition == 0) continue;
                if (!elemEndpoints.Contains(nd.ID)) continue;

                nd.Support.Pt = nd.Pt;
                nd.Support.Node = nd;
                shape.Supports.Add(nd.Support);
            }

            var initRule = rules?.OfType<SG_AutoRule_InitShape_3D>().FirstOrDefault();
            Vector3d loadVec = initRule?.LoadVector ?? new Vector3d(0, 0, -100);
            Vector3d areaLoadVec = initRule?.AreaLoadVector ?? Vector3d.Zero;

            shape.PointLoads ??= new List<SG_PointLoad>();
            shape.PointLoads.Clear();

            if (areaLoadVec.Length > 1e-12 && initRule != null)
            {
                ApplyVoronoiAreaLoads(shape, initRule, elemEndpoints, areaLoadVec, loadVec);
            }
            else
            {
                foreach (var nd in shape.Nodes)
                {
                    if (nd == null || !elemEndpoints.Contains(nd.ID)) continue;
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

            double totalArea = totalW * totalH;
            double cellArea = totalArea / (gridRes * gridRes);

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

        private static long HashPt(Point3d pt)
        {
            int x = (int)Math.Round(pt.X * 1000);
            int y = (int)Math.Round(pt.Y * 1000);
            int z = (int)Math.Round(pt.Z * 1000);
            return ((long)x << 40) ^ ((long)y << 20) ^ (long)z;
        }

        private static SG_Shape CloneShape(SG_Shape source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return source.DeepCopy();
        }

        private static TB_Model CloneModel(TB_Model source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return source.DeepCopy();
        }

        private static double Clamp01(double value)
        {
            return Math.Clamp(value, 0.0, 1.0);
        }
    }
}