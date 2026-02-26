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
        private int _topoMetricType = 0;
        private int _shapeMetricType = 0;

        private SG_GA _ga;
        private int _currentGeneration;
        private List<GAIndividual> _currentPopulation;
        private bool _isRunning;
        private List<List<GAIndividual>> _allGenerations;
        private List<List<SG_Shape>> _allShapes;
        private List<List<TB_Model>> _allModels;

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
            pManager.AddIntegerParameter("Topology Metric", "TopoMet",
                "Topology metric selector: 0=ElemCount, 1=NodeCount, 2=Elem/Node ratio, " +
                "3=AvgValence, 4=MaxValence, 5=LeafNodes, 6=BranchNodes, " +
                "7=Euler(V-E), 8=DistinctNames, 9=SupportCount",
                GH_ParamAccess.item, 0);                                                                                          // 14
            pManager.AddIntegerParameter("Shape Metric", "ShpeMet",
                "Shape metric selector: 0=TotalLength, 1=AvgLength, 2=MaxLength, " +
                "3=MinLength, 4=StdDevLength, 5=BBoxVolume, 6=BBoxDiagonal, " +
                "7=StructuralVolume, 8=MaxNodeSpan, 9=Compactness",
                GH_ParamAccess.item, 0);                                                                                           // 15
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
            pManager.AddNumberParameter("All Topology", "AllTopo", "Topology metric per individual {generation}(individual)", GH_ParamAccess.tree); // 10
            pManager.AddNumberParameter("All Shape", "AllShpe", "Shape metric per individual {generation}(individual)", GH_ParamAccess.tree); // 11

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

            // --- Metric selectors ---
            int topoMetricType = _topoMetricType;
            int shapeMetricType = _shapeMetricType;
            DA.GetData(14, ref topoMetricType);
            DA.GetData(15, ref shapeMetricType);
            _topoMetricType = Math.Clamp(topoMetricType, 0, TopologyMetrics.Count - 1);
            _shapeMetricType = Math.Clamp(shapeMetricType, 0, ShapeMetrics.Count - 1);

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
                _currentPopulation = _ga.CreateInitialGeneration(_populationSize, chromosomeLengths, ruleMarkers);
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
                List<GAIndividual> snapshot = evaluatedPop.Select(ind => ind.Clone()).ToList();
                _allGenerations.Add(snapshot);
                _allShapes.Add(evaluatedShapes.Where(s => s != null).Select(s => UT.DeepCopy(s)).ToList());
                _allModels.Add(evaluatedModels.Where(m => m != null).Select(m => CloneModel(m)).ToList());

                // Build cluster info for this generation
                clusterLogLines.Add(BuildClusterInfo(evaluatedPop, _currentGeneration));

                if (_currentGeneration < _numGenerations - 1)
                {
                    _currentPopulation = _ga.ProcessEvaluatedIndividuals(evaluatedPop);
                    _ga.IncrementGeneration();
                    _currentGeneration = _ga.CurrentGeneration;
                }
                else
                {
                    _ga.ProcessEvaluatedIndividuals(evaluatedPop);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        string.Format("Completed all {0} generations", _numGenerations));
                    break;
                }
            }

            // Output results
            OutputResults(DA, evaluatedPop, deep_copied_inishape, rls);
            DA.SetData(8, string.Join("\n", clusterLogLines));

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

                parts.Add(string.Format(
                    "  Cluster {0}: n={1}, BestFit={2}, AvgTopo={3:F1}, AvgShpe={4:F1}",
                    grp.Key, grp.Count(), bestStr, avgTopo, avgShpe));
            }

            return string.Join("\n", parts);
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
                // Clustering control
                TopoWeight = _topoWeight,
                ShapeWeight = _shapeWeight,
                FitnessWeight = _fitnessWeight,
                KMeansMaxIterations = _kmeansIterations,
                ReclusterInterval = _reclusterInterval
            };
            _currentGeneration = 0;
            _currentPopulation = null;
            _allGenerations = new List<List<GAIndividual>>();
            _allShapes = new List<List<SG_Shape>>();
            _allModels = new List<List<TB_Model>>();
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

                    TB_Model tb_mdl = new TB_Model(shape);
                    SolveLS slv = new SolveLS(ref tb_mdl);

                    double fitness = CalculateMaxNodalDisplacement(slv.Mdl);

                    individual.Fitness = fitness;

                    double topo = TopologyMetrics.Compute(shape, _topoMetricType);
                    double shpe = ShapeMetrics.Compute(shape, _shapeMetricType);

                    individual.Fitness = fitness;
                    individual.Topo = topo;
                    individual.Shpe = shpe;

                    evaluatedPop.Add(individual);
                    shapesOut.Add(UT.DeepCopy(shape));
                    modelsOut.Add(CloneModel(tb_mdl));
                }
                catch (Exception ex)
                {
                    individual.Fitness = MAXIMIZE ? double.MinValue : double.MaxValue;
                    individual.Topo = 0;
                    individual.Shpe = 0;
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
        /// Calculates topology metric for clustering using selected metric type.
        /// </summary>
        private double CalculateTopologyMetric(SG_Shape shape)
        {
            return TopologyMetrics.Compute(shape, _topoMetricType);
        }

        /// <summary>
        /// Calculates shape metric for clustering.
        /// </summary>
        private double CalculateShapeMetric(SG_Shape shape)
        {
            if (shape == null || shape.Elems == null)
                return 0.0;

            double totalLength = 0.0;
            foreach (var elem in shape.Elems)
            {
                if (elem is SG_Elem1D elem1d && elem1d.Crv != null)
                {
                    totalLength += elem1d.Crv.GetLength();
                }
            }

            return totalLength;
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

            GAIndividual best = MAXIMIZE
                ? evaluatedPop.OrderByDescending(i => i.Fitness).First()
                : evaluatedPop.OrderBy(i => i.Fitness).First();

            GH_Structure<GH_ObjectWrapper> shapesTree = new GH_Structure<GH_ObjectWrapper>();
            GH_Structure<GH_TB_Model> modelsTree = new GH_Structure<GH_TB_Model>();
            GH_Structure<GH_Number> fitnessTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Integer> clustGrpTree = new GH_Structure<GH_Integer>();
            GH_Structure<GH_Number> topoTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> shpeTree = new GH_Structure<GH_Number>();

            if (_allShapes != null && _allShapes.Count > 0)
            {
                for (int g = 0; g < _allShapes.Count; g++)
                {
                    GH_Path path = new GH_Path(g);
                    List<SG_Shape> genShapes = _allShapes[g];
                    List<TB_Model> genModels = (_allModels != null && g < _allModels.Count) ? _allModels[g] : null;
                    List<GAIndividual> genInds = (g < _allGenerations.Count) ? _allGenerations[g] : null;

                    for (int idx = 0; idx < genShapes.Count; idx++)
                    {
                        shapesTree.Append(new GH_ObjectWrapper(genShapes[idx]), path);

                        if (genModels != null && idx < genModels.Count)
                        {
                            modelsTree.Append(new GH_TB_Model(genModels[idx]), path);
                        }

                        if (genInds != null && idx < genInds.Count)
                        {
                            fitnessTree.Append(new GH_Number(genInds[idx].Fitness), path);
                            clustGrpTree.Append(new GH_Integer(genInds[idx].ClustGrp), path);
                            topoTree.Append(new GH_Number(genInds[idx].Topo), path);
                            shpeTree.Append(new GH_Number(genInds[idx].Shpe), path);
                        }
                    }
                }
            }

            string info = string.Format(
                "Generation: {0}/{1}\n" +
                "Population Size: {2}\n" +
                "Best Fitness: {3:F6}\n" +
                "Worst Fitness: {4:F6}\n" +
                "Mean Fitness: {5:F6}\n" +
                "Best Individual ID: {6}\n" +
                "Clustering: wTopo={7:F2} wShpe={8:F2} wFit={9:F2} KIter={10} ReClust={11}\n" +
                "Topology Metric: {12}\n" +
                "Shape Metric: {13}",
                _currentGeneration,
                _numGenerations,
                evaluatedPop.Count,
                best.Fitness,
                (MAXIMIZE ? evaluatedPop.OrderBy(i => i.Fitness).First().Fitness : evaluatedPop.OrderByDescending(i => i.Fitness).First().Fitness),
                evaluatedPop.Average(i => i.Fitness),
                best.Id,
                _topoWeight, _shapeWeight, _fitnessWeight,
                _kmeansIterations, _reclusterInterval,
                TopologyMetrics.GetLabel(_topoMetricType),
                ShapeMetrics.GetLabel(_shapeMetricType));

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

            model = new TB_Model(shape);
            SolveLS slv = new SolveLS(ref model);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_Generic; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("B4D6E8F1-2A3C-4D5E-9F1A-8B7C6D5E4F3A"); }
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