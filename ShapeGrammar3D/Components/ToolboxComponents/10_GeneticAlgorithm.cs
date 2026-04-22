using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;

namespace ShapeGrammar3D.Components.ToolboxComponents
{
    /// <summary>
    /// Grasshopper component for the genetic algorithm solver
    /// Manages population initialization, evaluation, and evolution
    /// </summary>
    public class GA_GeneticAlgorithm : GH_Component
    {
        private SG_GA _gaEngine;
        private List<GAIndividual> _currentGeneration;
        private bool _isRunning;
        private int _generationCounter;

        public GA_GeneticAlgorithm()
            : base("Genetic Algorithm", "GA",
                "Evolutionary optimization using genetic algorithms with clustering",
                Common.category, "Genetic Algorithm")
        {
            _gaEngine = new SG_GA();
            _currentGeneration = new List<GAIndividual>();
            _isRunning = false;
            _generationCounter = 0;
        }

        /// <summary>
        /// Registers all input parameters for the component
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Control parameters
            pManager.AddBooleanParameter("Initialize", "Init", "Initialize new population", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Run", "Run", "Run genetic algorithm iteration", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset", "Reset", "Reset algorithm state", GH_ParamAccess.item, false);

            // Configuration parameters
            pManager.AddIntegerParameter("Population Size", "PopSize", "Population size per cluster", GH_ParamAccess.item, 100);
            pManager.AddIntegerParameter("Number of Clusters", "NumClust", "Number of clusters for fitness landscape", GH_ParamAccess.item, 7);
            pManager.AddIntegerParameter("Max Generations", "MaxGen", "Maximum number of generations", GH_ParamAccess.item, 50);

            // GA parameters
            pManager.AddNumberParameter("Mutation Probability", "MutProb", "Probability of mutation (0-1)", GH_ParamAccess.item, 0.10);
            pManager.AddNumberParameter("Crossover Probability", "CrossProb", "Probability of crossover (0-1)", GH_ParamAccess.item, 0.9);
            pManager.AddNumberParameter("Elite Probability", "EliteProb", "Probability of elite selection (0-1)", GH_ParamAccess.item, 0.1);
            pManager.AddNumberParameter("BLX-Alpha", "BlxA", "BLX-alpha parameter for crossover", GH_ParamAccess.item, 0.3);
            pManager.AddBooleanParameter("Maximize", "Max", "Maximize (true) or minimize (false) fitness", GH_ParamAccess.item, false);

            // Chromosome structure
            pManager.AddIntegerParameter("Chromosome Lengths", "ChromoLen", "Lengths of each chromosome segment", GH_ParamAccess.list);

            // Fitness data from evaluation
            pManager.AddNumberParameter("Fitness Values", "Fitness", "Fitness values for current population", GH_ParamAccess.list);
            pManager.AddNumberParameter("Topology Metrics", "Topo", "Topology metrics for clustering", GH_ParamAccess.list);
            pManager.AddNumberParameter("Shape Metrics", "Shape", "Shape metrics for clustering", GH_ParamAccess.list);

            // Make fitness inputs optional
            for (int i = 11; i < pManager.ParamCount; i++)
            {
                pManager[i].Optional = true;
            }
        }

        /// <summary>
        /// Registers all output parameters for the component
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Population information
            pManager.AddTextParameter("Individual IDs", "IDs", "IDs of individuals in current population", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Chromosomes", "Chromo", "Chromosome values for each individual", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Chromosome Parameters", "Params", "Continuous parameters for each individual", GH_ParamAccess.tree);

            // Generation and fitness tracking
            pManager.AddIntegerParameter("Current Generation", "Gen", "Current generation number", GH_ParamAccess.item);
            pManager.AddNumberParameter("Best Fitness", "BestFit", "Best fitness found so far", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mean Fitness", "MeanFit", "Mean fitness of current generation", GH_ParamAccess.item);
            pManager.AddNumberParameter("Worst Fitness", "WorstFit", "Worst fitness of current generation", GH_ParamAccess.item);

            // Cluster information
            pManager.AddIntegerParameter("Cluster Groups", "ClustGrp", "Cluster group assignment for each individual", GH_ParamAccess.list);

            // Status
            pManager.AddTextParameter("Status", "Status", "Component status messages", GH_ParamAccess.item);

            // Per-individual clustering metrics
            pManager.AddNumberParameter("All Fitness", "AllFit", "Fitness value for each individual", GH_ParamAccess.list);
            pManager.AddNumberParameter("All Topology", "AllTopo", "Topology metric for each individual", GH_ParamAccess.list);
            pManager.AddNumberParameter("All Shape", "AllShpe", "Shape metric for each individual", GH_ParamAccess.list);
        }

        /// <summary>
        /// Main computation method
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Input variables ---
            bool initialize = false;
            bool run = false;
            bool reset = false;
            int populationSize = 100;
            int numClusters = 7;
            int maxGenerations = 50;
            double mutProb = 0.10;
            double crossProb = 0.9;
            double eliteProb = 0.1;
            double blxAlpha = 0.3;
            bool maximize = false;
            List<int> chromoLengths = new List<int>();
            List<double> fitnessValues = new List<double>();
            List<double> topoMetrics = new List<double>();
            List<double> shapeMetrics = new List<double>();

            // --- Get inputs ---
            if (!DA.GetData(0, ref initialize)) return;
            if (!DA.GetData(1, ref run)) return;
            if (!DA.GetData(2, ref reset)) return;
            if (!DA.GetData(3, ref populationSize)) return;
            if (!DA.GetData(4, ref numClusters)) return;
            if (!DA.GetData(5, ref maxGenerations)) return;
            if (!DA.GetData(6, ref mutProb)) return;
            if (!DA.GetData(7, ref crossProb)) return;
            if (!DA.GetData(8, ref eliteProb)) return;
            if (!DA.GetData(9, ref blxAlpha)) return;
            if (!DA.GetData(10, ref maximize)) return;
            if (!DA.GetDataList(11, chromoLengths)) return;

            // Optional: get fitness data
            DA.GetDataList(12, fitnessValues);
            DA.GetDataList(13, topoMetrics);
            DA.GetDataList(14, shapeMetrics);

            // --- Configure GA Engine ---
            _gaEngine.PopulationSize = populationSize;
            _gaEngine.NumClusters = numClusters;
            _gaEngine.NumGenerations = maxGenerations;
            _gaEngine.MutationProbability = mutProb;
            _gaEngine.CrossoverProbability = crossProb;
            _gaEngine.EliteProbability = eliteProb;
            _gaEngine.BlxAlpha = blxAlpha;
            _gaEngine.Maximize = maximize;

            string statusMessage = string.Empty;

            // --- Reset State ---
            if (reset)
            {
                _gaEngine = new SG_GA();
                _currentGeneration.Clear();
                _generationCounter = 0;
                _isRunning = false;
                statusMessage = "Algorithm reset. Ready for initialization.";
            }

            // --- Initialize Population ---
            if (initialize && !_isRunning)
            {
                try
                {
                    _currentGeneration = _gaEngine.CreateInitialGeneration(populationSize, chromoLengths);
                    _generationCounter = 0;
                    _isRunning = true;
                    statusMessage = string.Format("Initialized {0} individuals", populationSize);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, statusMessage);
                }
                catch (Exception ex)
                {
                    statusMessage = string.Format("Initialization failed: {0}", ex.Message);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, statusMessage);
                    _isRunning = false;
                }
            }

            // --- Run GA Iteration ---
            if (run && _isRunning && _currentGeneration.Count > 0)
            {
                try
                {
                    // Assign fitness values if provided
                    if (fitnessValues.Count == _currentGeneration.Count)
                    {
                        for (int i = 0; i < _currentGeneration.Count; i++)
                        {
                            _currentGeneration[i].Fitness = fitnessValues[i];
                        }
                    }

                    // Assign topology and shape metrics if provided
                    if (topoMetrics.Count == _currentGeneration.Count)
                    {
                        for (int i = 0; i < _currentGeneration.Count; i++)
                        {
                            _currentGeneration[i].Topo = topoMetrics[i];
                        }
                    }

                    if (shapeMetrics.Count == _currentGeneration.Count)
                    {
                        for (int i = 0; i < _currentGeneration.Count; i++)
                        {
                            _currentGeneration[i].Shpe = shapeMetrics[i];
                        }
                    }

                    // Cluster and evolve population
                    _currentGeneration = _gaEngine.ProcessEvaluatedIndividuals(_currentGeneration);
                    _generationCounter++;
                    _gaEngine.IncrementGeneration();

                    statusMessage = string.Format("Generation {0}/{1} completed. Population: {2}",
                        _generationCounter, maxGenerations, _currentGeneration.Count);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, statusMessage);

                    // Check if finished
                    if (_generationCounter >= maxGenerations)
                    {
                        _isRunning = false;
                        statusMessage = "Algorithm completed maximum generations.";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, statusMessage);
                    }
                }
                catch (Exception ex)
                {
                    statusMessage = string.Format("Evolution step failed: {0}", ex.Message);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, statusMessage);
                }
            }

            // --- Prepare Outputs ---
            try
            {
                // Individual IDs
                List<string> ids = _currentGeneration.Select(ind => ind.Id).ToList();
                DA.SetDataList(0, ids);

                // Chromosomes (as list of lists - flatten structure)
                List<int> chromoList = new List<int>();
                List<int> chromoIndices = new List<int>();
                
                for (int i = 0; i < _currentGeneration.Count; i++)
                {
                    chromoIndices.Add(chromoList.Count);
                    foreach (int gene in _currentGeneration[i].Chromosome)
                    {
                        chromoList.Add(gene);
                    }
                }
                DA.SetDataList(1, chromoList);

                // Chromosome Parameters (as list of lists - flatten structure)
                List<double> paramList = new List<double>();
                List<int> paramIndices = new List<int>();
                
                for (int i = 0; i < _currentGeneration.Count; i++)
                {
                    paramIndices.Add(paramList.Count);
                    foreach (double param in _currentGeneration[i].ChromosomeParam)
                    {
                        paramList.Add(param);
                    }
                }
                DA.SetDataList(2, paramList);

                // Current generation
                DA.SetData(3, _generationCounter);

                // Fitness statistics
                if (_currentGeneration.Count > 0)
                {
                    double bestFitness = _gaEngine.BestOfAll != null ? _gaEngine.BestOfAll.Fitness : -999;
                    double meanFitness = _currentGeneration.Average(ind => ind.Fitness);
                    double worstFitness = _gaEngine.Maximize
                        ? _currentGeneration.Min(ind => ind.Fitness)
                        : _currentGeneration.Max(ind => ind.Fitness);

                    DA.SetData(4, bestFitness);
                    DA.SetData(5, meanFitness);
                    DA.SetData(6, worstFitness);
                }

                // Cluster groups
                List<int> clusters = _currentGeneration.Select(ind => ind.ClustGrp).ToList();
                DA.SetDataList(7, clusters);

                // Status message
                DA.SetData(8, statusMessage);

                // Per-individual clustering metrics
                List<double> allFitness = _currentGeneration.Select(ind => ind.Fitness).ToList();
                List<double> allTopo = _currentGeneration.Select(ind => ind.Topo).ToList();
                List<double> allShape = _currentGeneration.Select(ind => ind.Shpe).ToList();
                DA.SetDataList(9, allFitness);
                DA.SetDataList(10, allTopo);
                DA.SetDataList(11, allShape);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                    string.Format("Output preparation failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Component icon
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // TODO: Replace with appropriate GA icon
                return Properties.Resources.icons_C_Sol_LS;
            }
        }

        /// <summary>
        /// Unique component GUID
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5c6"); }
        }
    }
}