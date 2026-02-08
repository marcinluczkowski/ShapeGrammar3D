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
    public class GrammarInterpreter_Auto : GH_Component
    {
        // Genetic Algorithm configuration constants
        private const int POPULATION_SIZE = 5;
        private const int NUM_GENERATIONS = 2;
        private const int NUM_CLUSTERS = 3;
        private const double MUTATION_PROBABILITY = 0.10;
        private const double CROSSOVER_PROBABILITY = 0.9;
        private const double ELITE_PROBABILITY = 0.1;
        private const bool MAXIMIZE = false; // Minimize displacement

        private SG_GA _ga;
        private int _currentGeneration;
        private List<GAIndividual> _currentPopulation;
        private bool _isRunning;
        private List<List<GAIndividual>> _allGenerations;
        private List<List<SG_Shape>> _allShapes;

        /// <summary>
        /// Initializes a new instance of the GrammerInterpreter_Auto class.
        /// </summary>
        public GrammarInterpreter_Auto()
          : base("GrammerInterpreter_Auto", "GI_Auto",
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
            pManager.AddNumberParameter("All Fitness", "All Fitness", "All fitness values", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Generation", "Gen", "Current generation number", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "GA information", GH_ParamAccess.item);

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

            SG_Shape ini_Shape = new SG_Shape();
            List<SG_Rule> rls = new List<SG_Rule>();
            bool reset = false;

            if (!DA.GetData(0, ref ini_Shape)) return;
            if (!DA.GetDataList(1, rls)) return;
            if (!DA.GetData(2, ref reset)) return;

            if (_ga == null || reset)
            {
                InitializeGA();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "GA initialized");
            }

            if (_isRunning)
            {
                DA.SetData(6, "GA is currently running. Please wait for completion.");
                return;
            }

            _isRunning = true;

            try
            {
                if (_currentPopulation == null)
                {
                    List<int> chromosomeLengths = GetChromosomeLengths(rls);
                    List<int> ruleMarkers = rls.Select(r => r.RuleMarker).ToList();
                    _currentPopulation = _ga.CreateInitialGeneration(POPULATION_SIZE, chromosomeLengths, ruleMarkers);
                    // AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    //     string.Format("Created initial population of {0} individuals", _currentPopulation.Count));
                }

                List<GAIndividual> evaluatedPop = null;
                List<SG_Shape> evaluatedShapes = null;

                SG_Shape deep_copied_inishape = new SG_Shape
                {
                    nodeCount = ini_Shape.nodeCount,
                    elementCount = ini_Shape.elementCount,

                    // deep copy needs update
                    Elems = ini_Shape.Elems.Select(e => e.DeepClone()).ToList(),
                    Nodes = ini_Shape.Nodes.Select(n => n.DeepClone()).ToList(),
                    Supports = ini_Shape.Supports.Select(s => s.DeepClone()).ToList(),
                    LineLoads = ini_Shape.LineLoads.Select(ll => (SG_LineLoad) ll.DeepClone()).ToList(),
                    PointLoads = ini_Shape.PointLoads.Select(pl => (SG_PointLoad) pl.DeepClone()).ToList(),
                    SimpleShapeState = ini_Shape.SimpleShapeState

                    // 
                    // 

                };

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



                    evaluatedShapes = new List<SG_Shape>();
                    evaluatedPop = EvaluatePopulation(_currentPopulation, deep_copied_inishape, rls, out evaluatedShapes);

                    List<GAIndividual> snapshot = evaluatedPop.Select(ind => ind.Clone()).ToList();
                    _allGenerations.Add(snapshot);
                    _allShapes.Add(evaluatedShapes.Select(s => UT.DeepCopy(s)).ToList());



                    // Process evaluated individuals and create next generation
                    if (_currentGeneration < NUM_GENERATIONS - 1)
                    {
                        _currentPopulation = _ga.ProcessEvaluatedIndividuals(evaluatedPop);
                        _ga.IncrementGeneration();
                        _currentGeneration = _ga.CurrentGeneration;
                    }
                    else
                    {
                        _ga.ProcessEvaluatedIndividuals(evaluatedPop);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            string.Format("Completed all {0} generations", NUM_GENERATIONS));
                        break;
                    }
                }

                // Output results
                OutputResults(DA, evaluatedPop, deep_copied_inishape, rls);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    string.Format("GA completed {0} generations", NUM_GENERATIONS));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GA error: " + ex.Message);
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// Initializes the genetic algorithm with constant parameters
        /// </summary>
        private void InitializeGA()
        {
            _ga = new SG_GA
            {
                PopulationSize = POPULATION_SIZE,
                NumGenerations = NUM_GENERATIONS,
                NumClusters = NUM_CLUSTERS,
                MutationProbability = MUTATION_PROBABILITY,
                CrossoverProbability = CROSSOVER_PROBABILITY,
                EliteProbability = ELITE_PROBABILITY,
                Maximize = MAXIMIZE,
                InitialBoost = 6,
                BlxAlpha = 0.3
            };
            _currentGeneration = 0;
            _currentPopulation = null;
            _allGenerations = new List<List<GAIndividual>>();
            _allShapes = new List<List<SG_Shape>>();
        }

        /// <summary>
        /// Gets chromosome lengths based on the number of rules
        /// </summary>
        private List<int> GetChromosomeLengths(List<SG_Rule> rules)
        {
            // Allocate genes per rule; adjust as needed per rule complexity
            var R = new Random();
            List<int> lengths = new List<int>();
            for (int i = 0; i < rules.Count; i++)
            {
                lengths.Add(R.Next(2,6)); // default length per rule
            }
            return lengths;
        }

        /// <summary>
        /// Evaluates a population of individuals
        /// </summary>
        private List<GAIndividual> EvaluatePopulation(List<GAIndividual> population, SG_Shape iniShape, List<SG_Rule> rules, out List<SG_Shape> shapesOut)
        {

            shapesOut = new List<SG_Shape>();

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
                    
                    
                    // SG_Shape shape = UT.DeepCopy(iniShape);

                    // added on 04 feb 2026
                    // deep copy function

                    SG_Shape shape = new SG_Shape
                    {
                        nodeCount = iniShape.nodeCount,
                        elementCount = iniShape.elementCount,

                        Elems = iniShape.Elems.Select(e => e.DeepClone()).ToList(),
                        Nodes = iniShape.Nodes,
                        Supports = iniShape.Supports,
                        LineLoads = iniShape.LineLoads,
                        PointLoads = iniShape.PointLoads,
                        SimpleShapeState = iniShape.SimpleShapeState
                    };


                    for (int j = 0; j < rules.Count; j++)
                    {
                        string message = rules[j].RuleOperation(ref shape, ref gt);
                    }

                    TB_Model tb_mdl = new TB_Model(shape);
                    SolveLS slv = new SolveLS(ref tb_mdl);

                    double fitness = CalculateMaxNodalDisplacement(slv.Mdl);

                    individual.Fitness = fitness;

                    double topo = CalculateTopologyMetric(shape);
                    double shpe = CalculateShapeMetric(shape);

                    individual.Fitness = fitness;
                    individual.Topo = topo;
                    individual.Shpe = shpe;

                    evaluatedPop.Add(individual);
                    shapesOut.Add(UT.DeepCopy(shape));
                }
                catch (Exception ex)
                {
                    individual.Fitness = MAXIMIZE ? double.MinValue : double.MaxValue;
                    individual.Topo = 0;
                    individual.Shpe = 0;
                    evaluatedPop.Add(individual);

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
                DA.SetData(6, "No evaluated individuals yet");
                return;
            }

            GAIndividual best = MAXIMIZE
                ? evaluatedPop.OrderByDescending(i => i.Fitness).First()
                : evaluatedPop.OrderBy(i => i.Fitness).First();

            GH_Structure<GH_ObjectWrapper> shapesTree = new GH_Structure<GH_ObjectWrapper>();
            GH_Structure<GH_Number> fitnessTree = new GH_Structure<GH_Number>();

            if (_allShapes != null && _allShapes.Count > 0)
            {
                for (int g = 0; g < _allShapes.Count; g++)
                {
                    GH_Path path = new GH_Path(g);
                    List<SG_Shape> genShapes = _allShapes[g];
                    List<GAIndividual> genInds = (g < _allGenerations.Count) ? _allGenerations[g] : null;

                    for (int idx = 0; idx < genShapes.Count; idx++)
                    {
                        shapesTree.Append(new GH_ObjectWrapper(genShapes[idx]), path);

                        if (genInds != null && idx < genInds.Count)
                        {
                            fitnessTree.Append(new GH_Number(genInds[idx].Fitness), path);
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
                NUM_GENERATIONS,
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
            DA.SetDataTree(4, fitnessTree);
            DA.SetData(5, _currentGeneration);
            DA.SetData(6, info);
        }

        private void RecreateShapeAndModel(GAIndividual individual, SG_Shape iniShape, List<SG_Rule> rules, out SG_Shape shape, out TB_Model model)
        {
            SG_Genotype gt = CreateGenotypeFromIndividual(individual);
            shape = UT.DeepCopy(iniShape);

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
                return null;// Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("38d35ef6-a3b2-44b2-bfa7-23d1292d37f5"); }
        }
    }
}