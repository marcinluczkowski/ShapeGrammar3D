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
using System.Linq;

namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class GrammarInterpreter_Auto4 : GH_Component
    {
        // Genetic Algorithm configuration (overridable from GH inputs)
        private int _populationSize = 5;
        private int _numGenerations = 3;
        private int _numClusters = 1;
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

        // Feasibility configuration
        private double _wDang = 0.20;
        private double _wAng = 0.0;
        private double _wLen = 0.0;

        // Cluster elite: guaranteed survivors per cluster per generation
        private int _clusterEliteCount = 0;

        // Multi-objective configuration
        private int _numObjectives = 1;

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
        private bool _isRunning;
        private List<List<GAIndividual>> _allGenerations;
        private List<List<SG_Shape>> _allShapes;
        private List<List<TB_Model>> _allModels;
        private GARunStore _runStore;

        /// <summary>
        /// Initializes a new instance of the GrammarInterpreter_Auto4 class.
        /// </summary>
        public GrammarInterpreter_Auto4()
          : base("GrammerInterpreter_Auto4", "GI_Auto4",
              "Automatic Grammar Interpreter with GA Optimization and Clustering Control",
              UT.CAT, UT.GR_INT)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // --- Original Auto2 inputs (0-8) ---
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "SG Assembly", GH_ParamAccess.item);                          // 0
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for Automatic Interpreter", GH_ParamAccess.list); // 1
            pManager.AddBooleanParameter("Reset", "Reset", "Reset genetic algorithm", GH_ParamAccess.item, false);               // 2
            pManager.AddIntegerParameter("Population Size", "Pop", "GA population size", GH_ParamAccess.item, 5);                // 3
            pManager.AddIntegerParameter("Generations", "Gen", "Number of GA generations", GH_ParamAccess.item, 3);              // 4
            pManager.AddIntegerParameter("Clusters", "Clusters", "Number of clusters", GH_ParamAccess.item, 1);                 // 5
            pManager.AddNumberParameter("Mutation Prob.", "Mut", "Mutation probability (0–1)", GH_ParamAccess.item, 0.10);       // 6
            pManager.AddNumberParameter("Crossover Prob.", "Cross", "Crossover probability (0–1)", GH_ParamAccess.item, 0.9);   // 7
            pManager.AddNumberParameter("Elite Prob.", "Elite", "Elite probability (0–1)", GH_ParamAccess.item, 0.1);            // 8

            // --- Clustering control inputs (9-13) ---
            pManager.AddNumberParameter("Topology Weight", "wTopo",
                "Weight for topology metric (element count) in clustering. 0 = ignore.", GH_ParamAccess.item, 1.0);               // 9
            pManager.AddNumberParameter("Shape Weight", "wShpe",
                "Weight for shape metric (total element length) in clustering. 0 = ignore.", GH_ParamAccess.item, 1.0);           // 10
            pManager.AddNumberParameter("Fitness Weight", "wFit",
                "Weight for fitness metric in clustering. 0 = ignore (default).", GH_ParamAccess.item, 0.0);                      // 11
            pManager.AddIntegerParameter("KMeans Iterations", "KIter",
                "Max iterations for KMeans centroid updates per generation.", GH_ParamAccess.item, 10);                            // 12
            pManager.AddIntegerParameter("Recluster Interval", "ReClust",
                "Re-initialize centroids every N generations. 0 = only at generation 0.", GH_ParamAccess.item, 5);                // 13
            pManager.AddIntegerParameter("Topology Metrics", "TopoMet",
                "Topology metric selectors (supply one or many for n-dimensional clustering):\n" +
                "0=ElemCount, 1=NodeCount, 2=Elem/Node ratio, " +
                "3=AvgValence, 4=MaxValence, 5=LeafNodes, 6=BranchNodes, " +
                "7=Euler(V-E), 8=DistinctNames, 9=SupportCount, " +
                "10=ConnectedComponents(b₀), 11=CycleRank(b₁), " +
                "12=MaxPipeIntersections, 13=AvgPipeIntersections",
                GH_ParamAccess.list);                                                                                              // 14
            pManager.AddIntegerParameter("Shape Metrics", "ShpeMet",
                "Shape metric selectors (supply one or many for n-dimensional clustering):\n" +
                "0=TotalLength, 1=AvgLength, 2=MaxLength, " +
                "3=MinLength, 4=StdDevLength, 5=BBoxVolume, 6=BBoxDiagonal, " +
                "7=StructuralVolume, 8=MaxNodeSpan, 9=Compactness",
                GH_ParamAccess.list);                                                                                              // 15
            pManager[14].Optional = true;
            pManager[15].Optional = true;
            pManager.AddBooleanParameter("Fixed Seed", "FixSeed",
                "Use a deterministic pre-generated population (same genotypes every run) " +
                "for controlled metric comparison experiments.",
                GH_ParamAccess.item, false);                                                                                        // 16
            pManager.AddNumberParameter("Dangling Weight", "wDang",
                "Weight for dangling-bar feasibility penalty (0..1). " +
                "Penalizes edges whose endpoint node has degree ≤ 1. " +
                "Applied as multiplicative fitness penalty: fit*(1+wDang*vDang). " +
                "Set 0 to disable.",
                GH_ParamAccess.item, 0.20);                                                                                         // 17
            pManager.AddNumberParameter("Angle Weight", "wAng",
                "Weight for angle-based feasibility penalty (0..1). Penalizes very small angles (<10°), optimal >=30°.",
                GH_ParamAccess.item, 0.0);                                                                                            // 18
            pManager.AddNumberParameter("Length Weight", "wLen",
                "Weight for element length penalty (0..1). Penalizes elements <0.5m or >10m (gentle).",
                GH_ParamAccess.item, 0.0);                                                                                            // 19
            pManager.AddIntegerParameter("Num Objectives", "nObj",
                "Number of objectives: 1 = single-objective (existing GA), " +
                "2 = bi-objective (displacement%SLS + avg utilization deviation), " +
                "3 = tri-objective (displacement%SLS + avg utilization deviation + feasibility). " +
                "Multi-objective uses NSGA-II.",
                GH_ParamAccess.item, 1);                                                                                               // 20
            pManager.AddBooleanParameter("Self Weight", "SW",
                "Include self-weight as lumped nodal point loads (half element weight at each end node). " +
                "Uses element length, cross-section area [mm²], and material density [kN/m³].",
                GH_ParamAccess.item, false);                                                                                            // 21
            pManager.AddIntegerParameter("CroSec Opt", "CSOpt",
                "Cross-section optimization mode:\n" +
                "0 = off\n" +
                "1 = solid Rect (50–1000 mm, 50 mm steps)\n" +
                "2 = SHS catalog (standard square hollow sections)\n" +
                "3 = HEA/HEB catalog (standard European I-sections)\n" +
                "4 = Combined SHS + HEA/HEB (best per element)",
                GH_ParamAccess.item, 0);                                                                                                // 22
            pManager.AddIntervalParameter("Metric Domains", "MDom",
                "Expected [min, max] domain per metric dimension for clustering normalization.\n" +
                "Order: topology metrics first, then shape metrics (same order as MetNm output).\n" +
                "Each value is mapped to [0, 1] via (val - min) / (max - min).\n" +
                "If not supplied, falls back to observed-max normalization.",
                GH_ParamAccess.list);                                                                                                    // 23
            pManager[23].Optional = true;
            pManager.AddVectorParameter("Gravity Dir", "GDir",
                "Direction of gravity for self-weight loads. " +
                "The vector is unitized internally; only direction matters.",
                GH_ParamAccess.item, new Vector3d(0, -1, 0));                                                                            // 24
            pManager.AddIntegerParameter("Cluster Elite", "ClElite",
                "Number of best individuals per cluster guaranteed to survive " +
                "into the next generation. Prevents cluster extinction.\n" +
                "0 = disabled (default). Typical value: 1–3.",
                GH_ParamAccess.item, 0);                                                                                                  // 25
            pManager.AddIntegerParameter("CSOpt Iterations", "CSIter",
                "Maximum FSD iterations for cross-section optimization.\n" +
                "Higher = better convergence to 90% utilization but slower.\n" +
                "Default: 40. Typical range: 10–100.",
                GH_ParamAccess.item, 40);                                                                                                  // 26
            pManager[26].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "Best SG Assembly", GH_ParamAccess.item);                      // 0
            pManager.AddParameter(new Param_TB_Model(), "TBModel", "TBModel", "Best TBModel", GH_ParamAccess.item);             // 1
            pManager.AddNumberParameter("Fitness", "Fitness", "Best fitness value (maximal nodal displacement)", GH_ParamAccess.item); // 2
            pManager.AddGenericParameter("All Shapes", "All Shapes", "All evaluated SG Assemblies", GH_ParamAccess.list);        // 3
            pManager.AddParameter(new Param_TB_Model(), "All Models", "All Models", "All evaluated TB Models", GH_ParamAccess.list); // 4
            pManager.AddNumberParameter("All Fitness", "All Fitness", "All fitness values", GH_ParamAccess.list);                // 5
            pManager.AddIntegerParameter("Generation", "Gen", "Current generation number", GH_ParamAccess.item);                 // 6
            pManager.AddTextParameter("Info", "Info", "GA information", GH_ParamAccess.item);                                    // 7
            pManager.AddTextParameter("Cluster Info", "ClustInfo", "Per-cluster statistics per generation", GH_ParamAccess.item); // 8
            pManager.AddIntegerParameter("ClustGrp", "Clust", "Cluster group per individual {generation}(individual)", GH_ParamAccess.tree); // 9
            pManager.AddNumberParameter("All Topology", "AllTopo", "First topology metric value per individual {generation}(individual)", GH_ParamAccess.tree); // 10
            pManager.AddNumberParameter("All Shape", "AllShpe", "First shape metric value per individual {generation}(individual)", GH_ParamAccess.tree); // 11
            pManager.AddNumberParameter("All Feasibility", "AllFeas",
                "Total feasibility violation per individual {generation}(individual)", GH_ParamAccess.tree);                          // 12
            pManager.AddNumberParameter("All VDang", "AllVDang",
                "Raw dangling bar penalty [0..1] per individual {generation}(individual)", GH_ParamAccess.tree);                     // 13
            pManager.AddNumberParameter("All VAng", "AllVAng",
                "Raw angle penalty [0..1] per individual {generation}(individual)", GH_ParamAccess.tree);                     // 14
            pManager.AddNumberParameter("All VLen", "AllVLen",
                "Raw length penalty [0..1] per individual {generation}(individual)", GH_ParamAccess.tree);                    // 15
            pManager.AddIntegerParameter("Pareto Rank", "Rank",
                "NSGA-II non-domination rank: 0=first Pareto front, 1=second, 2=third, etc. {generation}(individual). Multi-objective only.",
                GH_ParamAccess.tree);                                                                                         // 16
            pManager.AddNumberParameter("Obj Avg Util", "ObjUtil",
                "Average utilization deviation from 90% target per individual {generation}(individual). Multi-objective only.",
                GH_ParamAccess.tree);                                                                                         // 17
            pManager.AddNumberParameter("Obj Feasibility", "ObjFeas",
                "Raw feasibility score per individual {generation}(individual). " +
                "Average of VDang + VAng + VLen (unweighted, [0..1]). Tri-objective only.",
                GH_ParamAccess.tree);                                                                                         // 18
            pManager.AddTextParameter("JSON Path", "JSON",
                "Path to saved JSON file with full GA run data", GH_ParamAccess.item);                                        // 19
            pManager.AddNumberParameter("All Metrics", "AllMet",
                "All metric values per individual {generation;individual}(metric). " +
                "Order: topo metrics first, then shape metrics.",
                GH_ParamAccess.tree);                                                                                         // 20
            pManager.AddTextParameter("Metric Names", "MetNm",
                "Ordered metric axis labels (topology first, then shape)",
                GH_ParamAccess.list);                                                                                         // 21
            pManager.AddIntervalParameter("Metric Domains", "MDom",
                "Pass-through of user-supplied normalization domains (one per metric axis).\n" +
                "Connect to Radar Chart's MDom input for consistent normalization.",
                GH_ParamAccess.list);                                                                                         // 22
            pManager.AddNumberParameter("Crowding", "Crowd",
                "NSGA-II crowding distance per individual {generation}(individual). Selection: lower rank wins; same rank then higher crowding. Multi-objective only.",
                GH_ParamAccess.tree);                                                                                         // 23

            pManager[1].Optional = true;
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _ga = null;
            _currentGeneration = 0;
            _currentPopulation = null;
            _isRunning = false;
            _allGenerations = new List<List<GAIndividual>>();
            _allShapes = new List<List<SG_Shape>>();
            _allModels = new List<List<TB_Model>>();

            SG_Shape ini_Shape = new SG_Shape();
            List<SG_Rule> rls = new List<SG_Rule>();
            bool reset = false;

            if (!DA.GetData(0, ref ini_Shape)) return;
            if (!DA.GetDataList(1, rls)) return;
            if (!DA.GetData(2, ref reset)) return;

            // --- GA parameters ---
            int populationSize = _populationSize;
            int numGenerations = _numGenerations;
            int numClusters = _numClusters;
            double mutationProb = _mutationProbability;
            double crossoverProb = _crossoverProbability;
            double eliteProb = _eliteProbability;

            if (!DA.GetData(3, ref populationSize)) return;
            if (!DA.GetData(4, ref numGenerations)) return;
            if (!DA.GetData(5, ref numClusters)) return;
            if (!DA.GetData(6, ref mutationProb)) return;
            if (!DA.GetData(7, ref crossoverProb)) return;
            if (!DA.GetData(8, ref eliteProb)) return;

            _populationSize = Math.Max(1, populationSize);
            _numGenerations = Math.Max(1, numGenerations);
            _numClusters = Math.Max(1, numClusters);
            _mutationProbability = Clamp01(mutationProb);
            _crossoverProbability = Clamp01(crossoverProb);
            _eliteProbability = Clamp01(eliteProb);

            // --- Clustering parameters ---
            double topoWeight = _topoWeight;
            double shapeWeight = _shapeWeight;
            double fitnessWeight = _fitnessWeight;
            int kmeansIter = _kmeansIterations;
            int reclusterInterval = _reclusterInterval;

            DA.GetData(9, ref topoWeight);
            DA.GetData(10, ref shapeWeight);
            DA.GetData(11, ref fitnessWeight);
            DA.GetData(12, ref kmeansIter);
            DA.GetData(13, ref reclusterInterval);

            _topoWeight = Math.Max(0.0, topoWeight);
            _shapeWeight = Math.Max(0.0, shapeWeight);
            _fitnessWeight = Math.Max(0.0, fitnessWeight);
            _kmeansIterations = Math.Max(1, kmeansIter);
            _reclusterInterval = Math.Max(0, reclusterInterval);

            // --- Metric selectors (list-based for n-dimensional clustering) ---
            var rawTopoMetrics = new List<int>();
            var rawShpeMetrics = new List<int>();
            if (!DA.GetDataList(14, rawTopoMetrics) || rawTopoMetrics.Count == 0)
                rawTopoMetrics = new List<int> { 0 };
            if (!DA.GetDataList(15, rawShpeMetrics) || rawShpeMetrics.Count == 0)
                rawShpeMetrics = new List<int> { 0 };
            _topoMetricTypes = rawTopoMetrics
                .Select(v => Math.Clamp(v, 0, TopologyMetrics.Count - 1))
                .Distinct().ToList();
            _shapeMetricTypes = rawShpeMetrics
                .Select(v => Math.Clamp(v, 0, ShapeMetrics.Count - 1))
                .Distinct().ToList();

            // --- Fixed seed mode ---
            bool useFixedSeed = false;
            DA.GetData(16, ref useFixedSeed);

            // --- Feasibility parameters ---
            double wDang = _wDang;
            double wAng = _wAng;
            double wLen = _wLen;
            DA.GetData(17, ref wDang);
            DA.GetData(18, ref wAng);
            DA.GetData(19, ref wLen);
            _wDang = Math.Clamp(wDang, 0.0, 1.0);
            _wAng = Math.Clamp(wAng, 0.0, 1.0);
            _wLen = Math.Clamp(wLen, 0.0, 1.0);

            // --- Multi-objective ---
            int numObjectives = _numObjectives;
            DA.GetData(20, ref numObjectives);
            _numObjectives = Math.Clamp(numObjectives, 1, 3);

            bool isMultiObjective = _numObjectives > 1;

            // --- Self weight ---
            bool useSelfWeight = _useSelfWeight;
            DA.GetData(21, ref useSelfWeight);
            _useSelfWeight = useSelfWeight;

            Vector3d gravDir = _gravityDir;
            DA.GetData(24, ref gravDir);
            if (gravDir.Length > 1e-12)
            {
                gravDir.Unitize();
                _gravityDir = gravDir;
            }

            // --- Cross-section optimization ---
            int croSecOptMode = _croSecOptMode;
            DA.GetData(22, ref croSecOptMode);
            _croSecOptMode = Math.Clamp(croSecOptMode, 0, 4);

            // --- Metric normalization domains ---
            var rawDomains = new List<Grasshopper.Kernel.Types.GH_Interval>();
            if (DA.GetDataList(23, rawDomains) && rawDomains.Count > 0)
            {
                _metricDomains = rawDomains
                    .Select(d => d.Value)
                    .ToList();
            }
            else
            {
                _metricDomains = null;
            }

            // --- Cluster elite ---
            int clusterElite = _clusterEliteCount;
            DA.GetData(25, ref clusterElite);
            _clusterEliteCount = Math.Max(0, clusterElite);

            // --- CroSec optimization iterations ---
            int csIter = _croSecMaxIter;
            DA.GetData(26, ref csIter);
            _croSecMaxIter = Math.Clamp(csIter, 1, 500);

            // --- init GA ---
            if (_ga == null || reset)
            {
                InitializeGA();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "GA initialized");
            }

            if (_isRunning)
            {
                DA.SetData(7, "GA is currently running. Please wait for completion.");
                return;
            }

            _isRunning = true;

            SG_Shape deep_copied_inishape = CloneShape(ini_Shape);

            if (_currentPopulation == null)
            {
                List<int> chromosomeLengths = GetChromosomeLengths(rls, ini_Shape.Nodes?.Count ?? 11);
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

            List<GAIndividual> evaluatedPop = null;
            List<SG_Shape> evaluatedShapes = null;
            List<TB_Model> evaluatedModels = null;
            List<string> clusterLogLines = new List<string>();

            while (true)
            {
                deep_copied_inishape = CloneShape(ini_Shape);

                evaluatedShapes = new List<SG_Shape>();
                evaluatedPop = EvaluatePopulation(_currentPopulation, deep_copied_inishape, rls, out evaluatedShapes, out evaluatedModels);
                _allShapes.Add(evaluatedShapes.Where(s => s != null).Select(s => UT.DeepCopy(s)).ToList());
                _allModels.Add(evaluatedModels.Where(m => m != null).Select(m => CloneModel(m)).ToList());

                bool isLastGeneration = _currentGeneration >= _numGenerations - 1;

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
                    _ga.ClusterPopulation(evaluatedPop);

                // Cluster-elite injection for multi-objective mode:
                // after clustering, guarantee the best N individuals from
                // each cluster survive into the next generation's offspring.
                if (isMultiObjective && _clusterEliteCount > 0 && !isLastGeneration)
                {
                    _currentPopulation = InjectClusterElites(
                        _currentPopulation, evaluatedPop, _numClusters, _clusterEliteCount);
                }

                List<GAIndividual> snapshot = evaluatedPop.Select(ind => ind.Clone()).ToList();
                _allGenerations.Add(snapshot);

                _runStore.AppendGeneration(evaluatedPop, _currentGeneration);

                clusterLogLines.Add(isMultiObjective
                    ? BuildMOInfo(evaluatedPop, _currentGeneration)
                    : BuildClusterInfo(evaluatedPop, _currentGeneration));

                if (isLastGeneration)
                    break;
            }

            // Save JSON
            string jsonPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                string.Format("GA4_run_{0}.json", _runStore.RunId));
            try
            {
                _runStore.SaveToJson(jsonPath);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    string.Format("Failed to save JSON: {0}", ex.Message));
                jsonPath = string.Empty;
            }

            // Output results
            OutputResults(DA, evaluatedPop, deep_copied_inishape, rls);
            DA.SetData(8, string.Join("\n", clusterLogLines));
            DA.SetData(19, jsonPath);

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                string.Format("GA completed {0} generations", _numGenerations));
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
            _allGenerations = new List<List<GAIndividual>>();
            _allShapes = new List<List<SG_Shape>>();
            _allModels = new List<List<TB_Model>>();

            _runStore = new GARunStore
            {
                RunId = Guid.NewGuid().ToString("N").Substring(0, 8),
                StartedAt = DateTime.Now,
                PopulationSize = _populationSize,
                NumGenerations = _numGenerations,
                NumClusters = _numClusters,
                MutationProb = _mutationProbability,
                CrossoverProb = _crossoverProbability,
                EliteProb = _eliteProbability,
                NumObjectives = _numObjectives,
                UseSelfWeight = _useSelfWeight,
                UseCroSecOpt = _croSecOptMode > 0,
                TopoMetricTypes = new List<int>(_topoMetricTypes),
                ShapeMetricTypes = new List<int>(_shapeMetricTypes)
            };

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

        /// <summary>
        /// Gets chromosome lengths based on the number of rules.
        /// </summary>
        private List<int> GetChromosomeLengths(List<SG_Rule> rules, int nodeCount)
        {
            List<int> lengths = new List<int>();
            for (int i = 0; i < rules.Count; i++)
            {
                lengths.Add(Math.Max(11, nodeCount + 2));
            }
            return lengths;
        }

        /// <summary>
        /// Evaluates a population of individuals.
        /// Feasibility is computed on the graph (cheap) before expensive FEM.
        /// The penalty is applied multiplicatively: fitness * (1 + totalViolation).
        /// </summary>
        private List<GAIndividual> EvaluatePopulation(List<GAIndividual> population, SG_Shape iniShape, List<SG_Rule> rules, out List<SG_Shape> shapesOut, out List<TB_Model> modelsOut)
        {
            shapesOut = new List<SG_Shape>();
            modelsOut = new List<TB_Model>();

            List<GAIndividual> evaluatedPop = new List<GAIndividual>();

            if (population == null || population.Count == 0)
            {
                throw new InvalidOperationException("Population not initialized");
            }

            for (int i = 0; i < population.Count; i++)
            {
                GAIndividual individual = population[i];

                try
                {
                    SG_Genotype gt = CreateGenotypeFromIndividual(individual);

                    SG_Shape shape = CloneShape(iniShape);

                    for (int j = 0; j < rules.Count; j++)
                    {
                        string message = rules[j].RuleOperation(ref shape, ref gt);
                    }

                    shape.RegisterElemsToNodes();

                    if (_useSelfWeight)
                        ApplySelfWeightLoads(shape, _gravityDir);

                    // --- Feasibility check (graph-based, before expensive FEM) ---
                    FeasibilityResult feasResult = FeasibilityMetrics.Compute(shape, _wDang, _wAng, _wLen);

                    TB_Model tb_mdl = new TB_Model(shape);
                    SolveLS slv = new SolveLS(ref tb_mdl);
                    TB_Model finalModel = slv.Mdl;

                    if (_croSecOptMode == 1)
                        finalModel = OptimizeCrossSections(finalModel);
                    else if (_croSecOptMode == 2)
                        finalModel = OptimizeCrossSections_SHS(finalModel);
                    else if (_croSecOptMode == 3)
                        finalModel = OptimizeCrossSections_Combined(finalModel, heaOnly: true);
                    else if (_croSecOptMode == 4)
                        finalModel = OptimizeCrossSections_Combined(finalModel, heaOnly: false);

                    double rawDisp = CalculateMaxNodalDisplacement(finalModel);

                    var topoVals = _topoMetricTypes.Select(mt => TopologyMetrics.Compute(shape, mt)).ToList();
                    var shpeVals = _shapeMetricTypes.Select(mt => ShapeMetrics.Compute(shape, mt)).ToList();

                    double spanL = ComputeSpanL(finalModel);
                    double slsLimit = spanL / 300.0;
                    double dispRatio = (slsLimit > 1e-12) ? rawDisp / slsLimit : double.MaxValue;

                    double avgUtil = ComputeAverageUtilization(finalModel);
                    const double TARGET_UTIL = 0.90;
                    double utilDev = Math.Abs(avgUtil - TARGET_UTIL);
                    if (avgUtil > 1.0)
                        utilDev = (avgUtil - TARGET_UTIL) * 2.0;

                    if (_numObjectives > 1)
                    {
                        // Log-transform displacement so its scale is comparable
                        // to the [0,~1] range of the other objectives.
                        // log(1 + 0) = 0  (perfect),  log(1 + 1) ≈ 0.69,
                        // log(1 + 10) ≈ 2.4  (very bad).
                        double dispObj = Math.Log(1.0 + Math.Max(0.0, dispRatio));

                        // Raw feasibility: average of all three sub-penalties
                        // WITHOUT the single-objective user weights, so the
                        // score spans the full [0, 1] range and every aspect
                        // of feasibility contributes equally.
                        double rawFeas = (feasResult.VDang + feasResult.VAng + feasResult.VLen) / 3.0;
                        rawFeas = Math.Clamp(rawFeas, 0.0, 1.0);

                        individual.Fitness = dispRatio;
                        individual.ObjectiveValues = new List<double> { dispObj, utilDev };
                        if (_numObjectives >= 3)
                            individual.ObjectiveValues.Add(rawFeas);
                    }
                    else
                    {
                        double penalizedFitness = (rawDisp >= double.MaxValue || rawDisp <= double.MinValue)
                            ? rawDisp
                            : rawDisp * (1.0 + feasResult.TotalViolation);
                        individual.Fitness = penalizedFitness;
                    }

                    individual.TopoValues = topoVals;
                    individual.ShpeValues = shpeVals;
                    individual.Feas = feasResult.TotalViolation;
                    individual.VDang = feasResult.VDang;
                    individual.VAng = feasResult.VAng;
                    individual.VLen = feasResult.VLen;

                    evaluatedPop.Add(individual);
                    shapesOut.Add(UT.DeepCopy(shape));
                    modelsOut.Add(CloneModel(finalModel));
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

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Format("Individual {0} evaluation failed: {1}", i, ex.Message));
                }
            }

            return evaluatedPop;
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
        /// Outputs results to Grasshopper.
        /// </summary>
        private void OutputResults(IGH_DataAccess DA, List<GAIndividual> evaluatedPop, SG_Shape iniShape, List<SG_Rule> rules)
        {
            if (evaluatedPop == null || evaluatedPop.Count == 0)
            {
                DA.SetData(7, "No evaluated individuals yet");
                return;
            }

            bool isMultiObjective = _numObjectives > 1;

            GAIndividual best;
            if (isMultiObjective)
            {
                var frontZero = evaluatedPop.Where(i => i.Rank == 0).ToList();
                if (frontZero.Count > 0)
                    best = frontZero.OrderBy(i => i.Fitness).First();
                else
                    best = evaluatedPop.OrderBy(i => i.Fitness).First();
            }
            else
            {
                best = MAXIMIZE
                    ? evaluatedPop.OrderByDescending(i => i.Fitness).First()
                    : evaluatedPop.OrderBy(i => i.Fitness).First();
            }

            var shapesTree = new GH_Structure<GH_ObjectWrapper>();
            var modelsTree = new GH_Structure<GH_TB_Model>();
            var fitnessTree = new GH_Structure<GH_Number>();
            var clustGrpTree = new GH_Structure<GH_Integer>();
            var topoTree = new GH_Structure<GH_Number>();
            var shpeTree = new GH_Structure<GH_Number>();
            var feasTree = new GH_Structure<GH_Number>();
            var vDangTree = new GH_Structure<GH_Number>();
            var vAngTree = new GH_Structure<GH_Number>();
            var vLenTree = new GH_Structure<GH_Number>();
            var rankTree = new GH_Structure<GH_Integer>();
            var crowdingTree = new GH_Structure<GH_Number>();
            var objVolTree = new GH_Structure<GH_Number>();
            var objFeasTree = new GH_Structure<GH_Number>();
            var allMetricsTree = new GH_Structure<GH_Number>();

            if (_allShapes != null && _allShapes.Count > 0)
            {
                for (int g = 0; g < _allShapes.Count; g++)
                {
                    GH_Path path = new GH_Path(g);
                    List<SG_Shape> genShapes = _allShapes[g];
                    List<TB_Model> genModels = (_allModels != null && g < _allModels.Count) ? _allModels[g] : null;
                    List<GAIndividual> genInds = (g < _allGenerations.Count) ? _allGenerations[g] : null;

                    int count = genShapes.Count;
                    List<int> sortedOrder;
                    if (genInds != null && genInds.Count >= count)
                    {
                        sortedOrder = Enumerable.Range(0, count)
                            .OrderBy(i => genInds[i].ClustGrp)
                            .ThenBy(i => genInds[i].Fitness)
                            .ToList();
                    }
                    else
                    {
                        sortedOrder = Enumerable.Range(0, count).ToList();
                    }

                    for (int pos = 0; pos < sortedOrder.Count; pos++)
                    {
                        int idx = sortedOrder[pos];
                        shapesTree.Append(new GH_ObjectWrapper(genShapes[idx]), path);

                        if (genModels != null && idx < genModels.Count)
                            modelsTree.Append(new GH_TB_Model(genModels[idx]), path);

                        if (genInds != null && idx < genInds.Count)
                        {
                            var ind = genInds[idx];
                            fitnessTree.Append(new GH_Number(ind.Fitness), path);
                            clustGrpTree.Append(new GH_Integer(ind.ClustGrp), path);
                            topoTree.Append(new GH_Number(ind.Topo), path);
                            shpeTree.Append(new GH_Number(ind.Shpe), path);
                            feasTree.Append(new GH_Number(ind.Feas), path);
                            vDangTree.Append(new GH_Number(ind.VDang), path);
                            vAngTree.Append(new GH_Number(ind.VAng), path);
                            vLenTree.Append(new GH_Number(ind.VLen), path);

                            rankTree.Append(new GH_Integer(ind.Rank), path);
                            crowdingTree.Append(new GH_Number(ind.CrowdingDistance), path);
                            double objVol = (ind.ObjectiveValues != null && ind.ObjectiveValues.Count > 1)
                                ? ind.ObjectiveValues[1] : 0.0;
                            objVolTree.Append(new GH_Number(objVol), path);
                            double objFeas = (ind.ObjectiveValues != null && ind.ObjectiveValues.Count > 2)
                                ? ind.ObjectiveValues[2] : 0.0;
                            objFeasTree.Append(new GH_Number(objFeas), path);

                            var metPath = new GH_Path(g, pos);
                            if (ind.TopoValues != null)
                                foreach (double tv in ind.TopoValues)
                                    allMetricsTree.Append(new GH_Number(tv), metPath);
                            if (ind.ShpeValues != null)
                                foreach (double sv in ind.ShpeValues)
                                    allMetricsTree.Append(new GH_Number(sv), metPath);
                        }
                    }
                }
            }

            string topoLabels = string.Join(", ", _topoMetricTypes.Select(t => TopologyMetrics.GetLabel(t)));
            string shpeLabels = string.Join(", ", _shapeMetricTypes.Select(s => ShapeMetrics.GetLabel(s)));
            int clusterDims = _topoMetricTypes.Count + _shapeMetricTypes.Count + (_fitnessWeight > 0 ? 1 : 0);

            string info;
            if (isMultiObjective)
            {
                int frontZero = evaluatedPop.Count(ind => ind.Rank == 0);
                string objLabels = _numObjectives == 2
                    ? "log(1+Disp%SLS), AvgUtil Dev"
                    : "log(1+Disp%SLS), AvgUtil Dev, RawFeas(avg VDang+VAng+VLen)";
                info = string.Format(
                    "Mode: NSGA-II ({0} objectives: {1})\n" +
                    "Generation: {2}/{3}\n" +
                    "Population Size: {4}\n" +
                    "Pareto Front (rank 0) Size: {5}\n" +
                    "Best Displacement: {6:F6}\n" +
                    "Best Individual ID: {7}\n" +
                    "Topology Metrics ({8}D): {9}\n" +
                    "Shape Metrics ({10}D): {11}\n" +
                    "Clustering Dimensions: {12}",
                    _numObjectives, objLabels,
                    _currentGeneration, _numGenerations,
                    evaluatedPop.Count,
                    frontZero,
                    best.Fitness,
                    best.Id,
                    _topoMetricTypes.Count, topoLabels,
                    _shapeMetricTypes.Count, shpeLabels,
                    clusterDims);
            }
            else
            {
                info = string.Format(
                    "Mode: Single-Objective\n" +
                    "Generation: {0}/{1}\n" +
                    "Population Size: {2}\n" +
                    "Best Fitness: {3:F6}\n" +
                    "Worst Fitness: {4:F6}\n" +
                    "Mean Fitness: {5:F6}\n" +
                    "Best Individual ID: {6}\n" +
                    "Clustering: wTopo={7:F2} wShpe={8:F2} wFit={9:F2} KIter={10} ReClust={11}\n" +
                    "Topology Metrics ({12}D): {13}\n" +
                    "Shape Metrics ({14}D): {15}\n" +
                    "Clustering Dimensions: {16}\n" +
                    "Feasibility: wDang={17:F2}, BestVDang={18:F4}, BestFeas={19:F4}, AvgFeas={20:F4}",
                    _currentGeneration,
                    _numGenerations,
                    evaluatedPop.Count,
                    best.Fitness,
                    (MAXIMIZE ? evaluatedPop.OrderBy(i => i.Fitness).First().Fitness : evaluatedPop.OrderByDescending(i => i.Fitness).First().Fitness),
                    evaluatedPop.Average(i => i.Fitness),
                    best.Id,
                    _topoWeight, _shapeWeight, _fitnessWeight,
                    _kmeansIterations, _reclusterInterval,
                    _topoMetricTypes.Count, topoLabels,
                    _shapeMetricTypes.Count, shpeLabels,
                    clusterDims,
                    _wDang,
                    best.VDang,
                    best.Feas,
                    evaluatedPop.Average(i => i.Feas));
            }

            SG_Shape bestShape = null;
            TB_Model bestModel = null;

            try
            {
                RecreateShapeAndModel(best, iniShape, rules, out bestShape, out bestModel);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    string.Format("Failed to reconstruct best individual: {0}", ex.Message));
            }

            DA.SetData(0, bestShape);
            DA.SetData(1, bestModel != null ? new GH_TB_Model(bestModel) : null);
            DA.SetData(2, best.Fitness);
            DA.SetDataTree(3, shapesTree);
            DA.SetDataTree(4, modelsTree);
            DA.SetDataTree(5, fitnessTree);
            DA.SetData(6, _currentGeneration);
            DA.SetData(7, info);
            // Output 8 (ClustInfo) is set in SolveInstance after OutputResults
            DA.SetDataTree(9, clustGrpTree);
            DA.SetDataTree(10, topoTree);
            DA.SetDataTree(11, shpeTree);
            DA.SetDataTree(12, feasTree);
            DA.SetDataTree(13, vDangTree);
            DA.SetDataTree(14, vAngTree);
            DA.SetDataTree(15, vLenTree);
            DA.SetDataTree(16, rankTree);
            DA.SetDataTree(17, objVolTree);
            DA.SetDataTree(18, objFeasTree);
            DA.SetDataTree(23, crowdingTree);

            DA.SetDataTree(20, allMetricsTree);
            var metricNames = new List<string>();
            foreach (int t in _topoMetricTypes)
                metricNames.Add("T:" + TopologyMetrics.GetLabel(t));
            foreach (int s in _shapeMetricTypes)
                metricNames.Add("S:" + ShapeMetrics.GetLabel(s));
            DA.SetDataList(21, metricNames);

            if (_metricDomains != null && _metricDomains.Count > 0)
                DA.SetDataList(22, _metricDomains.Select(d => new GH_Interval(d)).ToList());
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
            get { return Properties.Resources.icons_CAT_Interpreter; }
        }
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
        {
            get { return new Guid("B4D6E8F1-2A3C-4D5E-9F1A-8B7C6D5E4F3A"); }
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
        /// Builds a combined catalog from SHS + HEA/HEB, sorted by area.
        /// Each element can independently pick whichever section type gives
        /// utilization closest to the target.
        /// </summary>
        private static CombinedEntry[] BuildCombinedCatalog()
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
        /// FSD optimization using a combined SHS + HEA/HEB catalog (mode 4)
        /// or HEA/HEB-only catalog (mode 3).
        /// Each element independently picks the section type that gives
        /// utilization closest to 90%.
        /// </summary>
        private TB_Model OptimizeCrossSections_Combined(TB_Model solvedModel, bool heaOnly)
        {
            if (solvedModel == null || solvedModel.Elem1Ds == null || solvedModel.Elem1Ds.Count == 0)
                return solvedModel;

            CombinedEntry[] catalog = heaOnly ? BuildHEACatalog() : BuildCombinedCatalog();
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