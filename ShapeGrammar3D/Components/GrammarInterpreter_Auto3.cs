using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GrammarInterpreter_Auto3 : GH_Component
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
        private GARunStore _runStore;

        public GrammarInterpreter_Auto3()
          : base("GrammerInterpreter_Auto3", "GI_Auto3",
              "Automatic Grammar Interpreter – lightweight genotype + fitness storage (JSON)",
              UT.CAT, UT.GR_INT)
        {
        }

        // ── Inputs (same as Auto2) ──────────────────────────────────────────
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

        // ── Outputs (minimal – data lives in JSON) ─────────────────────────
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "GA run summary", GH_ParamAccess.item);       // 0
            pManager.AddTextParameter("JSON Path", "JSON", "Path to saved JSON file", GH_ParamAccess.item); // 1
        }

        // ── Solve ───────────────────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _ga = null;
            _currentGeneration = 0;
            _currentPopulation = null;
            _isRunning = false;

            // --- inputs ---
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
            _mutationProbability = Math.Clamp(mutationProb, 0.0, 1.0);
            _crossoverProbability = Math.Clamp(crossoverProb, 0.0, 1.0);
            _eliteProbability = Math.Clamp(eliteProb, 0.0, 1.0);

            // --- init GA ---
            if (_ga == null || reset)
            {
                InitializeGA();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "GA initialized");
            }

            if (_isRunning)
            {
                DA.SetData(0, "GA is currently running. Please wait for completion.");
                return;
            }

            _isRunning = true;

            // --- create initial population ---
            if (_currentPopulation == null)
            {
                List<int> chromosomeLengths = GetChromosomeLengths(rls, ini_Shape.Nodes?.Count ?? 11);
                List<int> ruleMarkers = rls.Select(r => r.RuleMarker).ToList();
                _currentPopulation = _ga.CreateInitialGeneration(_populationSize, chromosomeLengths, ruleMarkers);
            }

            // --- run all generations ---
            while (true)
            {
                SG_Shape deep_copied_inishape = CloneShape(ini_Shape);

                List<GAIndividual> evaluatedPop = EvaluatePopulation(
                    _currentPopulation, deep_copied_inishape, rls);

                // Store lightweight records
                _runStore.AppendGeneration(evaluatedPop, _currentGeneration);

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

            // --- save JSON ---
            string jsonPath = Path.Combine(Path.GetTempPath(),
                string.Format("GA_run_{0}.json", _runStore.RunId));
            _runStore.SaveToJson(jsonPath);

            // --- outputs ---
            GAIndividual best = MAXIMIZE
                ? _currentPopulation.OrderByDescending(r => r.Fitness).First()
                : _currentPopulation.OrderBy(r => r.Fitness).First();

            string info = string.Format(
                "Generations: {0}\n" +
                "Population Size: {1}\n" +
                "Total Individuals: {2}\n" +
                "Best Fitness: {3:F6}  (Id {4})\n" +
                "Mean Fitness: {5:F6}\n" +
                "Clusters: {6}",
                _numGenerations,
                _populationSize,
                _currentPopulation.Count,
                best.Fitness, best.Id,
                _currentPopulation.Average(r => r.Fitness),
                _numClusters);

            DA.SetData(0, info);
            DA.SetData(1, jsonPath);

            _isRunning = false;
        }

        // ── GA initialisation ───────────────────────────────────────────────
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
            _runStore = new GARunStore
            {
                RunId = Guid.NewGuid().ToString("N").Substring(0, 8),
                StartedAt = DateTime.Now,
                PopulationSize = _populationSize,
                NumGenerations = _numGenerations,
                NumClusters = _numClusters,
                MutationProb = _mutationProbability,
                CrossoverProb = _crossoverProbability,
                EliteProb = _eliteProbability
            };
        }

        // ── Chromosome lengths (mirrors Auto2) ─────────────────────────────
        private List<int> GetChromosomeLengths(List<SG_Rule> rules, int nodeCount)
        {
            List<int> lengths = new List<int>();
            for (int i = 0; i < rules.Count; i++)
            {
                lengths.Add(Math.Max(11, nodeCount + 2));
            }
            return lengths;
        }

        // ── Evaluate population (no shape/model storage) ───────────────────
        private List<GAIndividual> EvaluatePopulation(
            List<GAIndividual> population, SG_Shape iniShape, List<SG_Rule> rules)
        {
            if (population == null || population.Count == 0)
                throw new InvalidOperationException("Population not initialized");

            List<GAIndividual> evaluatedPop = new List<GAIndividual>();
            int failCount = 0;

            for (int i = 0; i < population.Count; i++)
            {
                GAIndividual individual = population[i];

                try
                {
                    SG_Genotype gt = new SG_Genotype(
                        new List<int>(individual.Chromosome),
                        new List<double>(individual.ChromosomeParam));

                    SG_Shape shape = CloneShape(iniShape);

                    for (int j = 0; j < rules.Count; j++)
                    {
                        rules[j].RuleOperation(ref shape, ref gt);
                    }

                    shape.RegisterElemsToNodes();

                    TB_Model tb_mdl = new TB_Model(shape);
                    SolveLS slv = new SolveLS(ref tb_mdl);

                    individual.Fitness = CalculateMaxNodalDisplacement(slv.Mdl);
                    individual.Topo = CalculateTopologyMetric(shape);
                    individual.Shpe = CalculateShapeMetric(shape);

                    System.Diagnostics.Debug.WriteLine(
                        $"  [Auto3] Ind {i}: nodes={shape.Nodes?.Count}, " +
                        $"elems={shape.Elems?.Count}, supports={shape.Supports?.Count}, " +
                        $"fitness={individual.Fitness:E6}");
                }
                catch (Exception ex)
                {
                    failCount++;
                    individual.Fitness = MAXIMIZE ? double.MinValue : double.MaxValue;
                    individual.Topo = 0;
                    individual.Shpe = 0;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Format("Individual {0} evaluation failed: {1}", i, ex.Message));
                }

                evaluatedPop.Add(individual);
            }

            System.Diagnostics.Debug.WriteLine(
                $"  [Auto3] Generation eval complete: {failCount}/{population.Count} failed");

            return evaluatedPop;
        }

        // ── Fitness / metrics (same as Auto2) ──────────────────────────────
        private double CalculateMaxNodalDisplacement(TB_Model model)
        {
            double maxDisplacement = 0.0;

            if (model == null || model.Nodes == null || model.Nodes.Count == 0)
                return MAXIMIZE ? double.MinValue : double.MaxValue;

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
                            disp[2] * disp[2]);

                        if (displacement > maxDisplacement)
                            maxDisplacement = displacement;
                    }
                }
            }

            return maxDisplacement == 0.0
                ? (MAXIMIZE ? double.MinValue : double.MaxValue)
                : maxDisplacement;
        }

        private double CalculateTopologyMetric(SG_Shape shape)
        {
            if (shape == null || shape.Elems == null) return 0.0;
            return shape.Elems.Count;
        }

        private double CalculateShapeMetric(SG_Shape shape)
        {
            if (shape == null || shape.Elems == null) return 0.0;
            double totalLength = 0.0;
            foreach (var elem in shape.Elems)
            {
                if (elem is SG_Elem1D elem1d && elem1d.Crv != null)
                    totalLength += elem1d.Crv.GetLength();
            }
            return totalLength;
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static SG_Shape CloneShape(SG_Shape source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return source.DeepCopy();
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_Generic; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("A1C7E3F2-5B09-4D6A-8E1F-7C3A92D04B5E"); }
        }
    }
}