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
    public class GrammarInterpreter_Auto2 : GH_Component
    {
        // Genetic Algorithm configuration (overridable from GH inputs)
        private int _populationSize = 5;
        private int _numGenerations = 3;
        private int _numClusters = 1;
        private double _mutationProbability = 0.10;
        private double _crossoverProbability = 0.9;
        private double _eliteProbability = 0.1;
        private const bool MAXIMIZE = false; // Minimize displacement

        private SG_GA _ga;
        private int _currentGeneration;
        private List<GAIndividual> _currentPopulation;
        private bool _isRunning;
        private List<List<GAIndividual>> _allGenerations;
        private List<List<SG_Shape>> _allShapes;
        private List<List<TB_Model>> _allModels;

        /// <summary>
        /// Initializes a new instance of the GrammerInterpreter_Auto class.
        /// </summary>
        public GrammarInterpreter_Auto2()
          : base("GrammerInterpreter_Auto2", "GI_Auto",
              "Automatic Grammar Interpreter with Genetic Optimization and Linear Static Analysis",
              UT.CAT, UT.GR_INT)
        {

        }

        /// <summary>   
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "SG Assembly", GH_ParamAccess.item);
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for Automatic Interpreter", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Reset", "Reset", "Reset genetic algorithm", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Population Size", "Pop", "GA population size", GH_ParamAccess.item, 5);
            pManager.AddIntegerParameter("Generations", "Gen", "Number of GA generations", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Clusters", "Clusters", "Number of clusters", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Mutation Prob.", "Mut", "Mutation probability (0–1)", GH_ParamAccess.item, 0.10);
            pManager.AddNumberParameter("Crossover Prob.", "Cross", "Crossover probability (0–1)", GH_ParamAccess.item, 0.9);
            pManager.AddNumberParameter("Elite Prob.", "Elite", "Elite probability (0–1)", GH_ParamAccess.item, 0.1);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "Best SG Assembly", GH_ParamAccess.item);
            pManager.AddParameter(new Param_TB_Model(), "TBModel", "TBModel", "Best TBModel", GH_ParamAccess.item);
            pManager.AddNumberParameter("Fitness", "Fitness", "Best fitness value (maximal nodal displacement)", GH_ParamAccess.item);
            pManager.AddGenericParameter("All Shapes", "All Shapes", "All evaluated SG Assemblies", GH_ParamAccess.list);
            pManager.AddParameter(new Param_TB_Model(), "All Models", "All Models", "All evaluated TB Models", GH_ParamAccess.list);
            pManager.AddNumberParameter("All Fitness", "All Fitness", "All fitness values", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Generation", "Gen", "Current generation number", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "GA information", GH_ParamAccess.item);
            pManager.AddIntegerParameter("ClustGrp", "Clust", "Cluster group per individual {generation}(individual)", GH_ParamAccess.tree); // 8

            pManager[1].Optional = true;
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
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

//try
//{
if (_currentPopulation == null)
{
    List<int> chromosomeLengths = GetChromosomeLengths(rls, ini_Shape);
    List<int> ruleMarkers = rls.Select(r => r.RuleMarker).ToList();
    _currentPopulation = _ga.CreateInitialGeneration(_populationSize, chromosomeLengths, ruleMarkers);
    // AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
    //     string.Format("Created initial population of {0} individuals", _currentPopulation.Count));
}

List<GAIndividual> evaluatedPop = null;
List<SG_Shape> evaluatedShapes = null;
List<TB_Model> evaluatedModels = null;

while (true)
{

    // deep copy function

    //deep_copied_inishape = new SG_Shape
    //{
    //    nodeCount = iniShape.nodeCount,
    //    elementCount = iniShape.elementCount,

    //    Elems = iniShape.Elems,
    //    Nodes = iniShape.Nodes,
    //    Supports = iniShape.Supports,
    //    LineLoads = iniShape.LineLoads,
    //    PointLoads = iniShape.PointLoads,
    //    SimpleShapeState = iniShape.SimpleShapeState
    //};

    deep_copied_inishape = CloneShape(ini_Shape); // Fresh clone each generation

    evaluatedShapes = new List<SG_Shape>();
    evaluatedPop = EvaluatePopulation(_currentPopulation, deep_copied_inishape, rls, out evaluatedShapes, out evaluatedModels);
    List<GAIndividual> snapshot = evaluatedPop.Select(ind => ind.Clone()).ToList();
    _allGenerations.Add(snapshot);
    _allShapes.Add(evaluatedShapes.Where(s => s != null).Select(s => UT.DeepCopy(s)).ToList());
    _allModels.Add(evaluatedModels.Where(m => m != null).Select(m => CloneModel(m)).ToList());



    // Process evaluated individuals and create next generation
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
AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
    string.Format("GA completed {0} generations", _numGenerations));
// }
//catch (Exception ex)
//{

//    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GA error: " + ex.Message);
//}
//finally
//{
 //   _isRunning = false;
 //}
        }

        /// <summary>
        /// Initializes the genetic algorithm with constant parameters
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
                BlxAlpha = 0.3
            };
            _currentGeneration = 0;
            _currentPopulation = null;
            _allGenerations = new List<List<GAIndividual>>();
            _allShapes = new List<List<SG_Shape>>();
            _allModels = new List<List<TB_Model>>();
        }

        /// <summary>
        /// Gets chromosome lengths based on each rule's iteration target.
        /// </summary>
        private List<int> GetChromosomeLengths(List<SG_Rule> rules, SG_Shape shape)
        {
            List<int> lengths = new List<int>();
            for (int i = 0; i < rules.Count; i++)
            {
                lengths.Add(rules[i].GetChromosomeLength(shape));
            }
            return lengths;
        }

        /// <summary>
        /// Evaluates a population of individuals
        /// </summary>
        private List<GAIndividual> EvaluatePopulation(List<GAIndividual> population, SG_Shape iniShape, List<SG_Rule> rules, out List<SG_Shape> shapesOut, out List<TB_Model> modelsOut)
        {

            shapesOut = new List<SG_Shape>();
            modelsOut = new List<TB_Model>();

            List<GAIndividual> evaluatedPop = new List<GAIndividual>();

            //if (shapesOut == null)
            //{
            //    throw new ArgumentNullException(nameof(shapesOut));
            //}

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

                    System.Diagnostics.Debug.WriteLine(
                        $"  Ind {i}: nodes={shape.Nodes?.Count}, elems={shape.Elems?.Count}, " +
                        $"supports={shape.Supports?.Count}, fitness={fitness:E3}");

                    individual.Fitness = fitness;

                    double topo = CalculateTopologyMetric(shape);
                    double shpe = CalculateShapeMetric(shape);

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
                    shapesOut.Add(null);   // ← keep lists aligned
                    modelsOut.Add(null);   // ← keep lists aligned

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Format("Individual {0} evaluation failed: {1}", i, ex.Message));
                }
            }

            return evaluatedPop;
        }

        /// <summary>
        /// Creates a genotype from a GA individual
        /// </summary>
        private SG_Genotype CreateGenotypeFromIndividual(GAIndividual individual)
        {
            // Convert individual chromosome to genotype
            // SG_Genotype uses IntGenes and DGenes
            List<int> intGenes = new List<int>(individual.Chromosome);
            List<double> dGenes = new List<double>(individual.ChromosomeParam);

            SG_Genotype gt = new SG_Genotype(intGenes, dGenes);

            return gt;
        }

        /// <summary>
        /// Calculates the maximum nodal displacement from the analysis results
        /// </summary>
        private double CalculateMaxNodalDisplacement(TB_Model model)
        {
            double maxDisplacement = 0.0;

            if (model == null || model.Nodes == null || model.Nodes.Count == 0)
            {
                return MAXIMIZE ? double.MinValue : double.MaxValue; // Penalize invalid models
            }

            // Node.Disps is a List<double[]>, get the latest displacement
            foreach (var node in model.Nodes)
            {
                if (node.Disps != null && node.Disps.Count > 0)
                {
                    // Get the last displacement result
                    double[] disp = node.Disps.Last();

                    if (disp != null && disp.Length >= 3)
                    {
                        // Calculate total displacement magnitude (ignoring rotations)
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

            // Return penalty if no valid displacement found
            if (maxDisplacement == 0.0)
            {
                return MAXIMIZE ? double.MinValue : double.MaxValue;
            }

            return maxDisplacement;
        }

        /// <summary>
        /// Calculates topology metric for clustering
        /// </summary>
        private double CalculateTopologyMetric(SG_Shape shape)
        {
            // Simple metric: number of elements
            if (shape == null || shape.Elems == null)
                return 0.0;

            return shape.Elems.Count;
        }

        /// <summary>
        /// Calculates shape metric for clustering
        /// </summary>
        private double CalculateShapeMetric(SG_Shape shape)
        {
            // Simple metric: total length of elements
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
        /// Outputs results to Grasshopper
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
                        }
                    }
                }
            }

            // Prepare info string
            string info = string.Format(
                "Generation: {0}/{1}\n" +
                "Population Size: {2}\n" +
                "Best Fitness: {3:F6}\n" +
                "Worst Fitness: {4:F6}\n" +
                "Mean Fitness: {5:F6}\n" +
                "Best Individual ID: {6}",
                _currentGeneration,
                _numGenerations,
                evaluatedPop.Count,
                best.Fitness,
                (MAXIMIZE ? evaluatedPop.OrderBy(i => i.Fitness).First().Fitness : evaluatedPop.OrderByDescending(i => i.Fitness).First().Fitness),
                evaluatedPop.Average(i => i.Fitness),
                best.Id
            );

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
            DA.SetDataTree(8, clustGrpTree);
        }

        private void RecreateShapeAndModel(GAIndividual individual, SG_Shape iniShape, List<SG_Rule> rules, out SG_Shape shape, out TB_Model model)
        {
            SG_Genotype gt = CreateGenotypeFromIndividual(individual);
            // shape = iniShape;

            shape = new SG_Shape
            {
                nodeCount = iniShape.nodeCount,
                elementCount = iniShape.elementCount,

                // deep copy needs update
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

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.icons_Generic;// Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("38d35ef6-a3b2-44b2-bfa7-23d1292d22f5"); }
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