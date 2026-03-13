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
    public class GrammarInterpreter_Auto7 : GH_Component
    {
        // Genetic Algorithm configuration (overridable from GH inputs). Defaults for large runs (1000 pop, 100 gen).
        private int _populationSize = 1000;
        private int _numGenerations = 100;
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
        private bool _isRunning;
        // Memory-optimized: only first and last generation shapes stored; no full history.
        private List<SG_Shape> _firstGenShapes;
        private List<SG_Shape> _lastGenShapes;
        /// <summary>Per generation, per cluster: (gen, cluster, best, worst, avg) fitness for convergence plots.</summary>
        private List<(int gen, int cluster, double best, double worst, double avg)> _convergenceData;

        /// <summary>
        /// Large-scale GA interpreter: 1000 pop × 100 gen by default. Stores only first and last generation shapes and lightweight convergence data per cluster per generation.
        /// </summary>
        public GrammarInterpreter_Auto7()
          : base("GrammarInterpreter_Auto7", "GI_Auto7",
              "Large-scale GA (e.g. 1000×100). Outputs first and last generation shapes only; convergence data (best/worst/avg per cluster per gen) for plotting.",
              UT.CAT, UT.GR_INT)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "SG Assembly", GH_ParamAccess.item);                          // 0
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for Automatic Interpreter", GH_ParamAccess.list); // 1
            pManager.AddBooleanParameter("Reset", "Reset", "Reset genetic algorithm", GH_ParamAccess.item, false);               // 2
            pManager.AddParameter(new Param_GrammarInterpreterSettings(), "Settings", "Settings",
                "All GA/interpreter analysis settings packed in one object from GI_Settings component",
                GH_ParamAccess.item);                                                                                                     // 3
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "GA run summary", GH_ParamAccess.item);                                   // 0
            pManager.AddGenericParameter("First Gen", "First", "SG_Shape list for generation 0 (deep copies)", GH_ParamAccess.list);   // 1
            pManager.AddGenericParameter("Last Gen", "Last", "SG_Shape list for final generation (deep copies)", GH_ParamAccess.list);  // 2
            pManager.AddNumberParameter("Conv Best", "CBest", "Convergence: best fitness per cluster per generation. Path = (gen, cluster)", GH_ParamAccess.tree);  // 3
            pManager.AddNumberParameter("Conv Worst", "CWorst", "Convergence: worst fitness per cluster per generation. Path = (gen, cluster)", GH_ParamAccess.tree); // 4
            pManager.AddNumberParameter("Conv Avg", "CAvg", "Convergence: average fitness per cluster per generation. Path = (gen, cluster)", GH_ParamAccess.tree);   // 5
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
            _firstGenShapes = null;
            _lastGenShapes = null;
            _convergenceData = new List<(int gen, int cluster, double best, double worst, double avg)>();

            SG_Shape ini_Shape = new SG_Shape();
            List<SG_Rule> rls = new List<SG_Rule>();
            bool reset = false;

            if (!DA.GetData(0, ref ini_Shape)) return;
            if (!DA.GetDataList(1, rls)) return;
            if (!DA.GetData(2, ref reset)) return;

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
            if (DA.GetData(3, ref ghSettings) && ghSettings?.Value != null)
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

            _useSelfWeight = settings.SelfWeight;
            _croSecOptMode = settings.CroSecOpt;
            _metricDomains = settings.MetricDomains != null && settings.MetricDomains.Count > 0
                ? new List<Interval>(settings.MetricDomains)
                : null;
            _clusterEliteCount = settings.ClusterElite;
            _croSecMaxIter = settings.CSOptIterations;
            _gravityDir = settings.GravityDir;

            // --- init GA ---
            if (_ga == null || reset)
            {
                InitializeGA();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "GA initialized");
            }

            if (_isRunning)
            {
                DA.SetData(0, "GA is currently running. Please wait for completion.");
                DA.SetDataList(1, _firstGenShapes ?? new List<SG_Shape>());
                DA.SetDataList(2, _lastGenShapes ?? new List<SG_Shape>());
                DA.SetDataTree(3, new GH_Structure<GH_Number>());
                DA.SetDataTree(4, new GH_Structure<GH_Number>());
                DA.SetDataTree(5, new GH_Structure<GH_Number>());
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
            string lastClusterLog = string.Empty;

            while (true)
            {
                deep_copied_inishape = CloneShape(ini_Shape);

                evaluatedShapes = new List<SG_Shape>();
                evaluatedPop = EvaluatePopulation(_currentPopulation, deep_copied_inishape, rls, out evaluatedShapes, out evaluatedModels);

                // Cluster so ClustGrp is set before we record convergence (both single- and multi-objective)
                _ga.ClusterPopulation(evaluatedPop);
                AppendConvergenceForGeneration(evaluatedPop, _currentGeneration, _numClusters);

                if (_currentGeneration == 0)
                    _firstGenShapes = evaluatedShapes.Where(s => s != null).Select(s => UT.DeepCopy(s)).ToList();

                bool isLastGeneration = _currentGeneration >= _numGenerations - 1;
                if (isLastGeneration)
                    _lastGenShapes = evaluatedShapes.Where(s => s != null).Select(s => UT.DeepCopy(s)).ToList();

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
                    _ga.ClusterPopulation(evaluatedPop); // MOGA: cluster again for elite selection

                if (isMultiObjective && _clusterEliteCount > 0 && !isLastGeneration)
                {
                    _currentPopulation = InjectClusterElites(
                        _currentPopulation, evaluatedPop, _numClusters, _clusterEliteCount);
                }

                // Drop references to current gen data as soon as next gen is built so GC can reclaim memory
                evaluatedShapes?.Clear();
                evaluatedShapes = null;
                evaluatedModels?.Clear();
                evaluatedModels = null;
                evaluatedPop = null;

                if (isLastGeneration)
                    break;
            }

            OutputFirstLastAndConvergence(DA, lastClusterLog);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                string.Format("GA completed {0} generations", _numGenerations));
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
            _firstGenShapes = null;
            _lastGenShapes = null;
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
                    FeasibilityResult feasResult = FeasibilityMetrics.Compute(shape, _feasibilitySettings);

                    TB_Model tb_mdl = new TB_Model(shape);
                    SolveLS slv = new SolveLS(ref tb_mdl);
                    TB_Model finalModel = slv.Mdl;

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
                    double maxUtil = ComputeMaxUtilization(finalModel);

                    double rawFeas = (feasResult.VDang + feasResult.VAng + feasResult.VLen) / 3.0;
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
        /// Outputs first/last generation shapes and convergence trees for Auto7.
        /// </summary>
        private void OutputFirstLastAndConvergence(IGH_DataAccess DA, string lastClusterLog)
        {
            var firstList = _firstGenShapes ?? new List<SG_Shape>();
            var lastList = _lastGenShapes ?? new List<SG_Shape>();

            DA.SetDataList(1, firstList);
            DA.SetDataList(2, lastList);

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
            DA.SetDataTree(3, convBest);
            DA.SetDataTree(4, convWorst);
            DA.SetDataTree(5, convAvg);

            string topoLabels = string.Join(", ", _topoMetricTypes.Select(t => TopologyMetrics.GetLabel(t)));
            string shpeLabels = string.Join(", ", _shapeMetricTypes.Select(s => ShapeMetrics.GetLabel(s)));
            string info = string.Format(
                "GI_Auto7: {0} pop × {1} gen. First gen: {2} shapes, Last gen: {3} shapes. Convergence: {4} (gen,cluster) entries.\n\n{5}",
                _populationSize, _numGenerations,
                firstList.Count, lastList.Count,
                _convergenceData?.Count ?? 0,
                lastClusterLog);
            DA.SetData(0, info);
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