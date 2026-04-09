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
    public class GrammarInterpreter_Auto8 : GH_Component
    {
        private int _populationSize = 5;
        private int _numGenerations = 3;
        private int _numClusters = 1;
        private double _mutationProbability = 0.10;
        private double _crossoverProbability = 0.9;
        private double _eliteProbability = 0.1;
        private const bool MAXIMIZE = false;

        private double _topoWeight = 1.0;
        private double _shapeWeight = 1.0;
        private double _fitnessWeight = 0.0;
        private int _kmeansIterations = 10;
        private int _reclusterInterval = 5;
        private List<int> _topoMetricTypes = new List<int> { 0 };
        private List<int> _shapeMetricTypes = new List<int> { 0 };

        private FeasibilitySettings _feasibilitySettings = FeasibilitySettings.Default();

        private int _clusterEliteCount = 0;

        private int _numObjectives = 1;
        private int _singleObjType = 0;
        private int _utilObjType = 0;

        private bool _useSelfWeight = false;
        private Vector3d _gravityDir = new Vector3d(0, 0, -1);

        private int _croSecOptMode = 0;
        private int _croSecMaxIter = 40;

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

        public GrammarInterpreter_Auto8()
          : base("GrammarInterpreter_Auto8", "GI_Auto8",
              "GA Interpreter without SG_Shape input. Requires AutoRule_InitShape_3D as the first rule to build the initial shape.",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for Automatic Interpreter (first rule must be AutoRule_InitShape_3D)", GH_ParamAccess.list); // 0
            pManager.AddBooleanParameter("Reset", "Reset", "Reset genetic algorithm", GH_ParamAccess.item, false);               // 1
            pManager.AddParameter(new Param_GrammarInterpreterSettings(), "Settings", "Settings",
                "All GA/interpreter analysis settings packed in one object from GI_Settings component",
                GH_ParamAccess.item);                                                                                             // 2
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "GA run summary", GH_ParamAccess.item);
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly",
                "In-memory GA run assembly (genotypes, fitness, objectives, models) for Data Preview components",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _ga = null;
            _currentGeneration = 0;
            _currentPopulation = null;
            _isRunning = false;
            _allGenerations = new List<List<GAIndividual>>();
            _allShapes = new List<List<SG_Shape>>();
            _allModels = new List<List<TB_Model>>();

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

            rls = EnsureInitShapeFirst(rls);

            var ini_Shape = new SG_Shape
            {
                Nodes = new List<SG_Node>(),
                Supports = new List<SG_Support>(),
                PointLoads = new List<SG_PointLoad>(),
                LineLoads = new List<SG_LineLoad>()
            };

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
                DA.SetData(1, new GH_SGAssembly(new SGShapeGrammar3DAssembly()));
                return;
            }

            _isRunning = true;

            SG_Shape deep_copied_inishape = CloneShape(ini_Shape);

            if (_currentPopulation == null)
            {
                List<int> chromosomeLengths = GetChromosomeLengths(rls);
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

            string jsonPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                string.Format("GA8_run_{0}.json", _runStore.RunId));
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

            OutputAssemblyResults(DA, evaluatedPop, string.Join("\n", clusterLogLines));

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                string.Format("GA completed {0} generations", _numGenerations));
        }

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

        private List<int> GetChromosomeLengths(List<SG_Rule> rules)
        {
            var initRule = rules.OfType<SG_AutoRule_InitShape_3D>().FirstOrDefault();
            int estimatedNodeCount = initRule?.MaxSupports ?? 11;
            int fallbackLen = Math.Max(11, estimatedNodeCount + 2);

            var lengths = new List<int>();
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] is SG_AutoRule_InitShape_3D)
                {
                    lengths.Add(rules[i].GetChromosomeLength(new SG_Shape()));
                }
                else
                {
                    int ruleSpecific = rules[i].GetChromosomeLength(new SG_Shape());
                    lengths.Add(ruleSpecific < 11 ? Math.Max(2, ruleSpecific) : fallbackLen);
                }
            }
            return lengths;
        }

        private List<GAIndividual> EvaluatePopulation(List<GAIndividual> population, SG_Shape iniShape, List<SG_Rule> rules, out List<SG_Shape> shapesOut, out List<TB_Model> modelsOut)
        {
            shapesOut = new List<SG_Shape>();
            modelsOut = new List<TB_Model>();

            List<GAIndividual> evaluatedPop = new List<GAIndividual>();

            if (population == null || population.Count == 0)
            {
                throw new InvalidOperationException("Population not initialized");
            }

            int failCount = 0;
            string firstError = null;

            for (int i = 0; i < population.Count; i++)
            {
                GAIndividual individual = population[i];
                string _dbgStage = "Init";

                try
                {
                    SG_Genotype gt = CreateGenotypeFromIndividual(individual);

                    SG_Shape shape = CloneShape(iniShape);

                    _dbgStage = "RuleOps";
                    var ruleMessages = new List<string>();
                    for (int j = 0; j < rules.Count; j++)
                    {
                        _dbgStage = string.Format("Rule[{0}]={1}", j, rules[j].GetType().Name);
                        string message = rules[j].RuleOperation(ref shape, ref gt);
                        ruleMessages.Add(message);
                    }

                    _dbgStage = "RegisterElemsToNodes";
                    shape.RegisterElemsToNodes();

                    _dbgStage = "RepairSupportsAndLoads";
                    RepairSupportsAndLoads(shape, rules);

                    _dbgStage = "SelfWeight";
                    if (_useSelfWeight)
                        ApplySelfWeightLoads(shape, _gravityDir);

                    if (_currentGeneration == 0 && i == 0)
                    {
                        var diagParts = new List<string>();
                        diagParts.Add(string.Format("=== Individual #0 diagnostics ==="));
                        for (int ri = 0; ri < ruleMessages.Count; ri++)
                            diagParts.Add(string.Format("  Rule {0}: {1}", ri, ruleMessages[ri]));
                        diagParts.Add(string.Format("  Shape: {0} elems, {1} nodes, {2} supports, {3} loads",
                            shape.Elems?.Count ?? 0, shape.Nodes?.Count ?? 0,
                            shape.Supports?.Count ?? 0, shape.PointLoads?.Count ?? 0));
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            string.Join("\n", diagParts));
                    }

                    _dbgStage = "FeasibilityMetrics";
                    FeasibilityResult feasResult = FeasibilityMetrics.Compute(shape, _feasibilitySettings);

                    _dbgStage = "TB_Model";
                    TB_Model tb_mdl = new TB_Model(shape);
                    _dbgStage = "SolveLS";
                    SolveLS slv = new SolveLS(ref tb_mdl);
                    TB_Model finalModel = slv.Mdl;

                    _dbgStage = "CroSecOpt";
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

                    _dbgStage = "Metrics";
                    double rawDisp = CalculateMaxNodalDisplacement(finalModel);

                    var topoVals = _topoMetricTypes.Select(mt => TopologyMetrics.Compute(shape, mt)).ToList();
                    var shpeVals = _shapeMetricTypes.Select(mt => ShapeMetrics.Compute(shape, mt)).ToList();

                    double spanL = ComputeSpanL(finalModel);
                    double slsLimit = spanL / 300.0;
                    double dispRatio = (slsLimit > 1e-12) ? rawDisp / slsLimit : double.MaxValue;

                    if (_currentGeneration == 0 && i == 0)
                    {
                        int tbSupCount = finalModel?.Nodes?.Count(n => n.Sup != null) ?? 0;
                        int tbFreeCount = finalModel?.Nodes?.Count(n => n.Sup == null) ?? 0;
                        int hasDisps = finalModel?.Nodes?.Count(n => n.Disps != null && n.Disps.Count > 0) ?? 0;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            string.Format("  FEM: TB_Nodes={0} (sup={1}, free={2}), hasDisps={3}, rawDisp={4:E3}, spanL={5:F1}, dispRatio={6:E3}",
                                finalModel?.Nodes?.Count ?? 0, tbSupCount, tbFreeCount,
                                hasDisps, rawDisp, spanL, dispRatio));
                    }

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

                    _dbgStage = "DeepCopy";
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

                    failCount++;
                    if (firstError == null) firstError = string.Format("[Stage={0}] {1}", _dbgStage, ex.ToString());
                }
            }

            int successCount = population.Count - failCount;
            if (failCount > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    string.Format("Gen {0}: {1}/{2} succeeded, {3} failed. First error: {4}",
                        _currentGeneration, successCount, population.Count, failCount, firstError));
            }

            return evaluatedPop;
        }

        private SG_Genotype CreateGenotypeFromIndividual(GAIndividual individual)
        {
            List<int> intGenes = new List<int>(individual.Chromosome);
            List<double> dGenes = new List<double>(individual.ChromosomeParam);
            SG_Genotype gt = new SG_Genotype(intGenes, dGenes);
            return gt;
        }

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

        private void OutputAssemblyResults(IGH_DataAccess DA, List<GAIndividual> evaluatedPop, string clusterLogLines)
        {
            if (evaluatedPop == null || evaluatedPop.Count == 0)
            {
                DA.SetData(0, "No evaluated individuals yet");
                DA.SetData(1, new GH_SGAssembly(new SGShapeGrammar3DAssembly()));
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

            if (_allShapes != null && _allGenerations != null)
            {
                for (int g = 0; g < _allShapes.Count; g++)
                {
                    var genShapes = _allShapes[g];
                    var genModels = (_allModels != null && g < _allModels.Count) ? _allModels[g] : null;
                    var genInds = (g < _allGenerations.Count) ? _allGenerations[g] : null;
                    int count = genShapes.Count;
                    var sortedOrder = (genInds != null && genInds.Count >= count)
                        ? Enumerable.Range(0, count).OrderBy(i => genInds[i].ClustGrp).ThenBy(i => genInds[i].Fitness).ToList()
                        : Enumerable.Range(0, count).ToList();

                    var ag = new AssemblyGeneration { Generation = g };
                    for (int pos = 0; pos < sortedOrder.Count; pos++)
                    {
                        int idx = sortedOrder[pos];
                        var ind = (genInds != null && idx < genInds.Count) ? genInds[idx] : null;
                        var model = (genModels != null && idx < genModels.Count) ? genModels[idx] : null;
                        var shape = (idx < genShapes.Count) ? genShapes[idx] : null;
                        ag.Individuals.Add(AssemblyIndividual.FromGAIndividual(
                            ind ?? new GAIndividual(new List<int>(), new List<double>(), "?"),
                            model, shape));
                    }
                    assembly.Generations.Add(ag);
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
                    "Clustering Dimensions: {12}\n\n{13}",
                    _numObjectives, objLabels,
                    _currentGeneration, _numGenerations,
                    evaluatedPop.Count,
                    frontZero,
                    best.Fitness,
                    best.Id,
                    _topoMetricTypes.Count, topoLabels,
                    _shapeMetricTypes.Count, shpeLabels,
                    clusterDims,
                    clusterLogLines);
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
                    "Feasibility: wDang={17:F2}, BestVDang={18:F4}, BestFeas={19:F4}, AvgFeas={20:F4}\n\n{21}",
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
                    _feasibilitySettings.WDang,
                    best.VDang,
                    best.Feas,
                    evaluatedPop.Average(i => i.Feas),
                    clusterLogLines);
            }

            DA.SetData(0, info);
            DA.SetData(1, new GH_SGAssembly(assembly));
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_Generic; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("A8C1D2E3-F4A5-4B6C-9D0E-1F2A3B4C5D6E"); }
        }

        private struct PrecomputedSec
        {
            public double Area;
            public double Wy;
            public double Wz;
            public double Iy;
            public double Iz;
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
                maxN  = Math.Max(maxN,  Math.Max(Math.Abs(F[0]),  Math.Abs(F[6])));
                maxMy = Math.Max(maxMy, Math.Max(Math.Abs(F[4]),  Math.Abs(F[10])));
                maxMz = Math.Max(maxMz, Math.Max(Math.Abs(F[5]),  Math.Abs(F[11])));
                double nComp = Math.Min(F[0], F[6]);
                if (nComp < maxNcomp) maxNcomp = nComp;
            }
            return (maxN, maxMy, maxMz, maxNcomp);
        }

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
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

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

        private struct CombinedEntry
        {
            public PrecomputedSec Props;
            public bool IsI;
            public double H, W, Tw, Tf;
            public string Tag;
        }

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

        /// <summary>
        /// Rebuild supports from SG_Node.Support data, and ensure every element-endpoint
        /// node has a point load.  Call after all rules and RegisterElemsToNodes().
        /// </summary>
        private static void RepairSupportsAndLoads(SG_Shape shape, List<SG_Rule> rules)
        {
            if (shape?.Nodes == null) return;

            // --- Rebuild supports from node-level data ---
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

            // --- Ensure every element-endpoint node has a point load ---
            var initRule = rules?.OfType<SG_AutoRule_InitShape_3D>().FirstOrDefault();
            Vector3d loadVec = initRule?.LoadVector ?? new Vector3d(0, 0, -100);

            shape.PointLoads ??= new List<SG_PointLoad>();
            var loadedPts = new HashSet<long>();
            foreach (var pl in shape.PointLoads)
            {
                long key = HashPt(pl.Position);
                loadedPts.Add(key);
            }

            foreach (var nd in shape.Nodes)
            {
                if (nd == null || !elemEndpoints.Contains(nd.ID)) continue;
                long key = HashPt(nd.Pt);
                if (loadedPts.Contains(key)) continue;

                shape.PointLoads.Add(new SG_PointLoad(loadVec, Vector3d.Zero, nd.Pt));
                loadedPts.Add(key);
            }
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
    }
}
