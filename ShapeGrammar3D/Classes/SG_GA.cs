using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Rhino;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Individual represents a single candidate solution in the genetic algorithm.
    /// Contains chromosome (discrete values), chromosome parameters (continuous values),
    /// fitness metrics, and cluster group assignment.
    /// </summary>
    [Serializable]
    public class GAIndividual
    {
        private static int _globalIdCounter = 0;
        private static readonly object _idLock = new object();
        internal static readonly Random _rng = new Random();

        public List<int> Chromosome { get; set; }
        public List<double> ChromosomeParam { get; set; }
        public double Fitness { get; set; }
        public double Topo { get; set; }
        public double Shpe { get; set; }
        public int ClustGrp { get; set; }
        public string Id { get; set; }

        public GAIndividual(List<int> chromosome, List<double> chromosomeParam, string id = null)
        {
            Chromosome = new List<int>(chromosome);
            ChromosomeParam = new List<double>(chromosomeParam);
            Fitness = -999;
            Topo = -999;
            Shpe = -999;
            ClustGrp = -999;

            if (id == null)
            {
                lock (_idLock)
                {
                    Id = _globalIdCounter.ToString();
                    _globalIdCounter++;
                }
            }
            else
            {
                Id = id;
            }
        }

        /// <summary>
        /// Creates a copy of this individual
        /// </summary>
        public GAIndividual Clone()
        {
            GAIndividual cloned = new GAIndividual(
                new List<int>(Chromosome),
                new List<double>(ChromosomeParam),
                Id);
            cloned.Fitness = Fitness;
            cloned.Topo = Topo;
            cloned.Shpe = Shpe;
            cloned.ClustGrp = ClustGrp;
            return cloned;
        }

        /// <summary>
        /// Mutates a random gene in the chromosome parameter
        /// </summary>
        public void Mutate()
        {
            if (ChromosomeParam.Count > 0)
            {
                int idx = _rng.Next(0, ChromosomeParam.Count);
                ChromosomeParam[idx] = _rng.NextDouble(); // 0-1 range
            }
        }

        /// <summary>
        /// Exports chromosome to text format for transmission
        /// </summary>
        public string ExportChromosomeText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("S,");
            sb.Append(Id);
            sb.Append(",");

            // Append chromosome values
            foreach (int gene in Chromosome)
            {
                sb.Append(gene);
                sb.Append(",");
            }

            // Append chromosome parameters
            foreach (double gene in ChromosomeParam)
            {
                sb.Append(gene.ToString("F3"));
                sb.Append(",");
            }

            sb.Append("E,");
            sb.Append(ClustGrp);
            return sb.ToString();
        }

        /// <summary>
        /// Parses chromosome from text format
        /// </summary>
        public static GAIndividual ParseFromText(string text)
        {
            string[] parts = text.Split(',');

            if (parts.Length < 3 || parts[0] != "S")
                return null;

            int eIndex = Array.IndexOf(parts, "E");
            if (eIndex == -1)
                return null;

            string id = parts[1];
            List<int> chromo = new List<int>();
            List<double> param = new List<double>();

            // Parse chromosome and parameters
            int dataStart = 2;
            int dataEnd = eIndex;
            int splitPoint = dataStart + (dataEnd - dataStart) / 2;

            for (int i = dataStart; i < splitPoint; i++)
            {
                int val;
                if (int.TryParse(parts[i], out val))
                    chromo.Add(val);
            }

            for (int i = splitPoint; i < dataEnd; i++)
            {
                double val;
                if (double.TryParse(parts[i], out val))
                    param.Add(val);
            }

            GAIndividual individual = new GAIndividual(chromo, param, id);
            return individual;
        }
    }

    /// <summary>
    /// Genetic Algorithm solver for optimization with clustering support
    /// </summary>
    [Serializable]
    public class SG_GA
    {
        // Configuration constants
        public bool Maximize { get; set; } = false;
        public int PopulationSize { get; set; } = 100;
        public int NumClusters { get; set; } = 7;
        public int InitialBoost { get; set; } = 6;
        public int NumGenerations { get; set; } = 50;
        public double MutationProbability { get; set; } = 0.10;
        public double CrossoverProbability { get; set; } = 0.9;
        public double EliteProbability { get; set; } = 0.1;
        public double BlxAlpha { get; set; } = 0.3;

        // Clustering configuration
        public double TopoWeight { get; set; } = 1.0;
        public double ShapeWeight { get; set; } = 1.0;
        public double FitnessWeight { get; set; } = 0.0;
        public int KMeansMaxIterations { get; set; } = 10;
        public int ReclusterInterval { get; set; } = 0; // 0 = only at generation 0

        // State variables
        private List<GAIndividual> _currentPopulation;
        private List<double> _bestFits;
        private List<double> _worstFits;
        private GAIndividual _bestOfAll;
        private int _currentGeneration;
        private double _topoDenominator;
        private double _shapeDenominator;
        private double _fitnessDenominator;

        // Tracking templates for chromosome generation
        private List<int> _chromosomeTemplateLengths = new List<int>();
        private List<int> _ruleMarkersTemplate = new List<int>();

        // Data tracking
        private List<GAIndividual> _allEvaluatedIndividuals;
        private string _outputFilePath;

        // Clustering
        private SimpleKMeans _kmeans;

        public SG_GA()
        {
            _currentPopulation = new List<GAIndividual>();
            _bestFits = new List<double>();
            _worstFits = new List<double>();
            _bestOfAll = null;
            _currentGeneration = 0;
            _allEvaluatedIndividuals = new List<GAIndividual>();
            _outputFilePath = Path.Combine(Path.GetTempPath(),
                string.Format("GA_output_{0:yyyy-MM-dd_HH-mm-ss}.csv", DateTime.Now));
        }

        /// <summary>
        /// Creates initial generation with random chromosome values
        /// </summary>
        /// 
        
        
        public List<GAIndividual> CreateInitialGeneration(int populationCount, List<int> chromosomeLengths)
        {
            List<GAIndividual> generation = new List<GAIndividual>();

            for (int cnt = 0; cnt < populationCount; cnt++)
            {
                List<int> chromosome = new List<int>();
                List<double> chromosomeParam = new List<double>();

                for (int i = 0; i < chromosomeLengths.Count; i++)
                {
                    int len = chromosomeLengths[i];
                    int ruleId = GetRuleId(i); // Map rule indices

                    chromosome.Add(ruleId);
                    chromosomeParam.Add(ruleId);

                    for (int j = 0; j < len; j++)
                    {
                        chromosome.Add(GAIndividual._rng.Next(0, 2));
                        chromosomeParam.Add(GAIndividual._rng.NextDouble());
                    }

                    chromosome.Add(-999);
                    chromosomeParam.Add(-999);
                }

                generation.Add(new GAIndividual(chromosome, chromosomeParam));
            }

            return generation;
        }
        

        /// <summary>|
        /// Creates initial generation with explicit rule markers.
        /// Uses stratified activation with per-rule bias:
        /// - Rule 011 (num struts per node) and Rule 02 (struts): ~80% activation
        ///   because they are the primary structural drivers.
        /// - Rule 041 (bars) and Rule 051: high activation (70%-90%).
        /// - Rule 01 (subdivision): moderate-high (40%-70%) to ensure enough
        ///   node diversity for strut attachment.
        /// - Rule 031 (rotation): lower activation (20%-60%).
        /// - Other rules use the default range (30%-90%).
        /// The population is shuffled after generation to avoid ordering bias.
        /// </summary>
        public List<GAIndividual> CreateInitialGeneration(int populationCount, List<int> chromosomeLengths, List<int> ruleMarkers)
        {
            if (chromosomeLengths == null) throw new ArgumentNullException(nameof(chromosomeLengths));
            if (ruleMarkers == null) throw new ArgumentNullException(nameof(ruleMarkers));
            if (chromosomeLengths.Count != ruleMarkers.Count)
            {
                throw new ArgumentException("chromosomeLengths and ruleMarkers must have the same count.");
            }

            _chromosomeTemplateLengths = new List<int>(chromosomeLengths);
            _ruleMarkersTemplate = new List<int>(ruleMarkers);

            List<GAIndividual> generation = new List<GAIndividual>();

            for (int cnt = 0; cnt < populationCount; cnt++)
            {
                // Base stratified activation: linearly spread across the population
                double t = (double)cnt / Math.Max(1, populationCount - 1);

                List<int> chromosome = new List<int>();
                List<double> chromosomeParam = new List<double>();

                for (int i = 0; i < chromosomeLengths.Count; i++) // loop for each rule
                {
                    int len = chromosomeLengths[i];
                    int ruleId = ruleMarkers[i];

                    // Per-rule activation bias:
                    double activationProb;
                    if (ruleId == UT.RULE011_MARKER || ruleId == UT.RULE020_MARKER)
                    {
                        // Core structural rules (strut count + strut creation): 75% to 85%
                        activationProb = 0.75 + 0.10 * t;
                    }
                    else if (ruleId == UT.RULE041_MARKER || ruleId == UT.RULE051_MARKER)
                    {
                        // Secondary structural rules (bars, rule051): 70% to 90%
                        activationProb = 0.70 + 0.20 * t;
                    }
                    else if (ruleId == UT.RULE010_MARKER)
                    {
                        // Subdivision: 40% to 70% — needs to be high enough
                        // to create diverse node layouts for struts to attach to
                        activationProb = 0.40 + 0.30 * t;
                    }
                    else if (ruleId == UT.RULE031_MARKER)
                    {
                        // Rotation: 20% to 60%
                        activationProb = 0.20 + 0.40 * t;
                    }
                    else
                    {
                        // Other rules: 30% to 90%
                        activationProb = 0.30 + 0.60 * t;
                    }

                    chromosome.Add(ruleId);
                    chromosomeParam.Add(ruleId);

                    for (int j = 0; j < len; j++)
                    {
                        chromosome.Add(GAIndividual._rng.NextDouble() < activationProb ? 1 : 0);
                        chromosomeParam.Add(GAIndividual._rng.NextDouble());
                    }

                    chromosome.Add(UT.RULE_END_MARKER);
                    chromosomeParam.Add(UT.RULE_END_MARKER);
                }

                generation.Add(new GAIndividual(chromosome, chromosomeParam));
            }

            // Shuffle to remove ordering bias from stratified activation
            for (int i = generation.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (generation[i], generation[j]) = (generation[j], generation[i]);
            }

            return generation;
        }

        /// <summary>
        /// Maps rule index to rule ID (from Python code)
        /// </summary>
        private int GetRuleId(int index)
        {
            if (index == 0) return -10;
            if (index == 1) return -11;
            if (index == 2) return -20;
            if (index == 3) return -30;
            if (index == 4) return -31;
            if (index == 5) return -41;
            if (index == 6) return -51;
            if (index == 7) return -60;
            if (index == 8) return -61;
            return -(index + 1);
        }

        /// <summary>
        /// Performs one generation of genetic algorithm
        /// </summary>
        public List<GAIndividual> SolveOneGeneration(List<GAIndividual> individuals)
        {
            if (individuals.Count == 0)
                return new List<GAIndividual>();

            List<GAIndividual> previousGeneration = individuals.Select(i => i.Clone()).ToList();
            int clustGroup = individuals[0].ClustGrp;
            int popSize = individuals.Count;

            // Step 1: Evaluate fitness
            GAIndividual best = FindBest(individuals);
            GAIndividual worst = FindWorst(individuals);

            _bestFits.Add(best.Fitness);
            _worstFits.Add(worst.Fitness);

            double mean = individuals.Average(i => i.Fitness);

            if (_bestOfAll == null)
                _bestOfAll = best.Clone();
            else if (Maximize && best.Fitness > _bestOfAll.Fitness)
                _bestOfAll = best.Clone();
            else if (!Maximize && best.Fitness < _bestOfAll.Fitness)
                _bestOfAll = best.Clone();

            System.Diagnostics.Debug.WriteLine(
                string.Format("Generation {0}, Cluster {1}: Best: {2:F3}, Worst: {3:F3}, Mean: {4:F3}",
                    _currentGeneration, clustGroup, best.Fitness, worst.Fitness, mean));

            List<GAIndividual> newGeneration = new List<GAIndividual>();

            // Step 2: Elitism - preserve best evaluated individuals (at least 1)
            int numElites = Math.Max(1, (int)(popSize * EliteProbability));
            List<GAIndividual> elites = SelectElite(individuals, numElites);
            newGeneration.AddRange(elites);

            // Step 3: Crossover - create offspring from evaluated parents
            int numChildren = (int)(popSize * CrossoverProbability);
            List<GAIndividual> children = Crossover(individuals, numChildren);
            newGeneration.AddRange(children);

            // Step 4: Mutation - inject fresh random individuals for diversity
            EnsureTemplatesFromPopulation(individuals);
            int numMutated = (int)(popSize * MutationProbability);
            List<int> mutLengths = _chromosomeTemplateLengths.Count > 0 ? _chromosomeTemplateLengths : new List<int> { 1 };
            List<int> mutMarkers = (_ruleMarkersTemplate.Count == mutLengths.Count && mutLengths.Count > 0)
                ? _ruleMarkersTemplate
                : mutLengths.Select((_, idx) => GetRuleId(idx)).ToList();
            if (numMutated > 0)
            {
                List<GAIndividual> mutated = CreateInitialGeneration(numMutated, mutLengths, mutMarkers);
                newGeneration.AddRange(mutated);
            }

            // Step 5: Fill remaining spots via tournament on EVALUATED population only
            if (newGeneration.Count < popSize)
            {
                int remaining = popSize - newGeneration.Count;
                List<GAIndividual> tournamentSelected = SelectTournament(individuals, remaining);
                newGeneration.AddRange(tournamentSelected);
            }

            // Trim to exact population size (elites are at the front, so they survive)
            if (newGeneration.Count > popSize)
                newGeneration = newGeneration.Take(popSize).ToList();

            // Assign cluster group
            foreach (GAIndividual individual in newGeneration)
            {
                individual.ClustGrp = clustGroup;
            }

            return newGeneration.Count > 0 ? newGeneration : previousGeneration;
        }

        /// <summary>
        /// Performs clustering on population based on weighted topology, shape, and fitness metrics.
        /// </summary>
        public void ClusterPopulation(List<GAIndividual> individuals)
        {
            if (individuals.Count == 0)
                return;

            List<double> topoValues = individuals.Select(i => i.Topo).ToList();
            List<double> shapeValues = individuals.Select(i => i.Shpe).ToList();
            List<double> fitnessValues = individuals.Select(i => i.Fitness).ToList();

            // Compute current-generation maxima for normalization
            double topoMax = topoValues.Max();
            double shapeMax = shapeValues.Max();
            double fitnessMax = fitnessValues
                .Where(f => !double.IsInfinity(f) && !double.IsNaN(f)
                         && f != double.MaxValue && f != double.MinValue)
                .DefaultIfEmpty(1.0)
                .Max();

            if (topoMax <= 0) topoMax = 1.0;
            if (shapeMax <= 0) shapeMax = 1.0;
            if (fitnessMax <= 0) fitnessMax = 1.0;

            bool shouldInitialize = _currentGeneration == 0
                || _kmeans == null
                || (ReclusterInterval > 0 && _currentGeneration % ReclusterInterval == 0);

            if (shouldInitialize)
            {
                _topoDenominator = topoMax;
                _shapeDenominator = shapeMax;
                _fitnessDenominator = fitnessMax;
                _kmeans = new SimpleKMeans(NumClusters);
            }

            // Build weighted, normalized data points
            List<double[]> dataPoints = new List<double[]>(individuals.Count);
            for (int i = 0; i < individuals.Count; i++)
            {
                double normTopo = (_topoDenominator > 0)
                    ? topoValues[i] / _topoDenominator * TopoWeight : 0.0;
                double normShape = (_shapeDenominator > 0)
                    ? shapeValues[i] / _shapeDenominator * ShapeWeight : 0.0;

                double normFitness = 0.0;
                if (FitnessWeight > 0)
                {
                    double f = fitnessValues[i];
                    if (!double.IsInfinity(f) && !double.IsNaN(f)
                        && f != double.MaxValue && f != double.MinValue
                        && _fitnessDenominator > 0)
                    {
                        normFitness = f / _fitnessDenominator * FitnessWeight;
                    }
                }

                dataPoints.Add(new double[] { normTopo, normShape, normFitness });
            }

            List<int> clusters = _kmeans.Cluster(dataPoints, shouldInitialize, KMeansMaxIterations);

            for (int i = 0; i < clusters.Count && i < individuals.Count; i++)
            {
                individuals[i].ClustGrp = clusters[i];
            }
        }

        /// <summary>
        /// Processes evaluated individuals and updates population
        /// </summary>
        public List<GAIndividual> ProcessEvaluatedIndividuals(List<GAIndividual> evaluated)
        {
            ClusterPopulation(evaluated);
            _currentPopulation = evaluated;
            _allEvaluatedIndividuals.AddRange(evaluated);

            List<GAIndividual> newGeneration = new List<GAIndividual>();

            // Process each cluster separately
            for (int clustIdx = 0; clustIdx < NumClusters; clustIdx++)
            {
                List<GAIndividual> clusterIndividuals = evaluated
                    .Where(i => i.ClustGrp == clustIdx)
                    .ToList();

                if (clusterIndividuals.Count > 0)
                {
                    List<GAIndividual> newClusterGen = SolveOneGeneration(clusterIndividuals);
                    newGeneration.AddRange(newClusterGen);
                }
            }

            return newGeneration;
        }

        /// <summary>
        /// Exports best individuals from each cluster
        /// </summary>
        public List<GAIndividual> GetBestsFromClusters(int numBests)
        {
            List<GAIndividual> result = new List<GAIndividual>();

            for (int i = 0; i < NumClusters; i++)
            {
                List<GAIndividual> clusterBests = _currentPopulation
                    .Where(p => p.ClustGrp == i)
                    .OrderBy(p => p.Fitness)
                    .Take(numBests)
                    .ToList();

                result.AddRange(clusterBests);
            }

            return result;
        }

        private GAIndividual FindBest(List<GAIndividual> individuals)
        {

            return Maximize
                ? individuals.OrderByDescending(i => i.Fitness).First()
                : individuals.OrderBy(i => i.Fitness).First();
        }

        private GAIndividual FindWorst(List<GAIndividual> individuals)
        {
            return Maximize
                ? individuals.OrderBy(i => i.Fitness).First()
                : individuals.OrderByDescending(i => i.Fitness).First();
        }

        private List<GAIndividual> SelectElite(List<GAIndividual> individuals, int count)
        {
            return individuals
                .OrderBy(i => i.Fitness)
                .Take(count)
                .Select(i => i.Clone())
                .ToList();
        }

        private List<GAIndividual> SelectTournament(List<GAIndividual> individuals, int count)
        {
            List<GAIndividual> selected = new List<GAIndividual>();

            for (int i = 0; i < count - 1; i++)
            {
                int sampleSize = Math.Min(3, individuals.Count);
                List<GAIndividual> tournament = individuals
                    .OrderBy(x => Random.Shared.Next())
                    .Take(sampleSize)
                    .ToList();

                GAIndividual winner = FindBest(tournament);
                selected.Add(winner.Clone());
            }
            if (individuals.Count == 0)
            {
                RhinoApp.WriteLine("Individuals are null");
            }
            selected.Add(FindBest(individuals).Clone());
            return selected;
        }

        private List<GAIndividual> Crossover(List<GAIndividual> individuals, int numChildren)
        {
            List<GAIndividual> children = new List<GAIndividual>();

            for (int i = 0; i < numChildren; i++)
            {
                List<GAIndividual> parents = individuals
                    .OrderBy(x => Random.Shared.Next())
                    .Take(2)
                    .ToList();

                if (parents.Count == 2)
                {
                    GAIndividual child1, child2;
                    TwoPointCrossover(parents[0], parents[1], out child1, out child2);

                    List<double> paramChild1, paramChild2;
                    CrossoverBlxAlpha(parents[0], parents[1], out paramChild1, out paramChild2);

                    child1.ChromosomeParam = AlignChromosomeParams(child1, paramChild1);
                    child2.ChromosomeParam = AlignChromosomeParams(child2, paramChild2);

                    children.Add(child1);
                    children.Add(child2);
                }
            }

            return children;
        }

        /// <summary>
        /// Ensures chromosome parameters align with the chromosome length and preserve markers.
        /// </summary>
        private List<double> AlignChromosomeParams(GAIndividual owner, List<double> candidateParams)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            int length = owner.Chromosome.Count;
            List<double> result = new List<double>(length);

            for (int i = 0; i < length; i++)
            {
                double value = 0.5;

                if (candidateParams != null && i < candidateParams.Count)
                {
                    value = candidateParams[i];
                }

                if (owner.Chromosome[i] < 0)
                {
                    value = owner.Chromosome[i];
                }
                else
                {
                    value = Clamp(value, 0.0, 1.0);
                }

                result.Add(value);
            }

            return result;
        }

        private void TwoPointCrossover(GAIndividual parent1, GAIndividual parent2, out GAIndividual child1, out GAIndividual child2)
        {
            int size = parent1.Chromosome.Count;
            if (size < 2)
            {
                child1 = parent1.Clone();
                child2 = parent2.Clone();
                return;
            }

            List<int> chromo1 = new List<int>(parent1.Chromosome);
            List<int> chromo2 = new List<int>(parent2.Chromosome);

            int point1 = Random.Shared.Next(1, size);
            int point2 = Random.Shared.Next(1, size - 1);
            if (point2 >= point1)
                point2++;
            else
            {
                int temp = point1;
                point1 = point2;
                point2 = temp;
            }

            for (int i = point1; i < point2; i++)
            {
                int tmp = chromo1[i];
                chromo1[i] = chromo2[i];
                chromo2[i] = tmp;
            }

            child1 = new GAIndividual(chromo1, parent1.ChromosomeParam.ToList());
            child2 = new GAIndividual(chromo2, parent2.ChromosomeParam.ToList());
        }

        private void CrossoverBlxAlpha(GAIndividual parent1, GAIndividual parent2, out List<double> child1, out List<double> child2)
        {
            List<double> p1 = new List<double>(parent1.ChromosomeParam);
            List<double> p2 = new List<double>(parent2.ChromosomeParam);

            child1 = new List<double>();
            child2 = new List<double>();

            if (p1.Count == 0)
            {
                return;
            }

            // Remove invalid genes (outside [0, 1])
            List<int> invalidIndices = new List<int>();
            List<double> invalidValues = new List<double>();

            for (int i = p1.Count - 1; i >= 0; i--)
            {
                if (p1[i] < 0 || p1[i] > 1)
                {
                    invalidIndices.Insert(0, i);
                    invalidValues.Insert(0, p1[i]);
                    p1.RemoveAt(i);
                    p2.RemoveAt(i);
                }
            }

            for (int i = 0; i < p1.Count; i++)
            {
                double minX = Math.Min(p1[i], p2[i]);
                double maxX = Math.Max(p1[i], p2[i]);
                double dx = Math.Abs(p1[i] - p2[i]);

                double minCx = minX - BlxAlpha * dx;
                double maxCx = maxX + BlxAlpha * dx;

                double gene1 = (maxCx - minCx) * Random.Shared.NextDouble() + minCx;
                double gene2 = (maxCx - minCx) * Random.Shared.NextDouble() + minCx;

                gene1 = Clamp(gene1, 0.0, 1.0);
                gene2 = Clamp(gene2, 0.0, 1.0);

                child1.Add(gene1);
                child2.Add(gene2);
            }

            // Restore invalid genes
            for (int i = 0; i < invalidIndices.Count; i++)
            {
                child1.Insert(invalidIndices[i], invalidValues[i]);
                child2.Insert(invalidIndices[i], invalidValues[i]);
            }
        }

        /// <summary>
        /// Clamps a value between min and max (replacement for Math.Clamp in C# 7.3)
        /// </summary>
        private double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// K-Means clustering with K-means++ initialization, multi-dimensional support,
        /// and iterative centroid updates.
        /// </summary>
        private class SimpleKMeans
        {
            private int _numClusters;
            private List<double[]> _centroids;
            private Random _random;

            public SimpleKMeans(int numClusters)
            {
                _numClusters = Math.Max(1, numClusters);
                _centroids = new List<double[]>();
                _random = new Random();
            }

            /// <summary>
            /// Clusters the data points. When initialize is true, centroids are
            /// seeded using K-means++. Runs up to maxIterations of assign-update.
            /// </summary>
            public List<int> Cluster(List<double[]> data, bool initialize, int maxIterations)
            {
                if (data.Count == 0)
                    return new List<int>();

                if (initialize || _centroids.Count != _numClusters)
                {
                    InitializeCentroidsPlusPlus(data);
                }

                List<int> assignments = AssignToClusters(data);

                for (int iter = 0; iter < maxIterations; iter++)
                {
                    UpdateCentroids(data, assignments);
                    List<int> newAssignments = AssignToClusters(data);

                    bool converged = true;
                    for (int i = 0; i < assignments.Count; i++)
                    {
                        if (assignments[i] != newAssignments[i])
                        {
                            converged = false;
                            break;
                        }
                    }

                    assignments = newAssignments;
                    if (converged) break;
                }

                return assignments;
            }

            /// <summary>
            /// K-means++ seeding: first centroid random, subsequent centroids
            /// chosen with probability proportional to squared distance.
            /// </summary>
            private void InitializeCentroidsPlusPlus(List<double[]> data)
            {
                _centroids.Clear();

                int firstIdx = _random.Next(data.Count);
                _centroids.Add((double[])data[firstIdx].Clone());

                for (int c = 1; c < _numClusters; c++)
                {
                    double[] distances = new double[data.Count];
                    double totalDist = 0;

                    for (int i = 0; i < data.Count; i++)
                    {
                        double minDist = double.MaxValue;
                        foreach (double[] centroid in _centroids)
                        {
                            double dist = SquaredDistance(data[i], centroid);
                            if (dist < minDist) minDist = dist;
                        }
                        distances[i] = minDist;
                        totalDist += minDist;
                    }

                    if (totalDist == 0)
                    {
                        _centroids.Add((double[])data[_random.Next(data.Count)].Clone());
                        continue;
                    }

                    double r = _random.NextDouble() * totalDist;
                    double cumulative = 0;
                    bool added = false;
                    for (int i = 0; i < data.Count; i++)
                    {
                        cumulative += distances[i];
                        if (cumulative >= r)
                        {
                            _centroids.Add((double[])data[i].Clone());
                            added = true;
                            break;
                        }
                    }

                    if (!added)
                        _centroids.Add((double[])data[data.Count - 1].Clone());
                }
            }

            private List<int> AssignToClusters(List<double[]> data)
            {
                List<int> assignments = new List<int>(data.Count);
                foreach (double[] point in data)
                {
                    int bestCluster = 0;
                    double bestDist = double.MaxValue;

                    for (int c = 0; c < _centroids.Count; c++)
                    {
                        double dist = SquaredDistance(point, _centroids[c]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCluster = c;
                        }
                    }

                    assignments.Add(bestCluster);
                }
                return assignments;
            }

            private void UpdateCentroids(List<double[]> data, List<int> assignments)
            {
                int dims = data[0].Length;

                for (int c = 0; c < _numClusters; c++)
                {
                    List<double[]> clusterPoints = new List<double[]>();
                    for (int i = 0; i < assignments.Count; i++)
                    {
                        if (assignments[i] == c)
                            clusterPoints.Add(data[i]);
                    }

                    if (clusterPoints.Count > 0)
                    {
                        double[] newCentroid = new double[dims];
                        for (int d = 0; d < dims; d++)
                        {
                            double sum = 0;
                            for (int p = 0; p < clusterPoints.Count; p++)
                                sum += clusterPoints[p][d];
                            newCentroid[d] = sum / clusterPoints.Count;
                        }
                        _centroids[c] = newCentroid;
                    }
                }
            }

            private double SquaredDistance(double[] p1, double[] p2)
            {
                double sum = 0;
                int dims = Math.Min(p1.Length, p2.Length);
                for (int d = 0; d < dims; d++)
                {
                    double diff = p1[d] - p2[d];
                    sum += diff * diff;
                }
                return sum;
            }
        }

        public void IncrementGeneration()
        {
            _currentGeneration++;
        }

        public int CurrentGeneration { get { return _currentGeneration; } }
        public GAIndividual BestOfAll { get { return _bestOfAll; } }
        public List<GAIndividual> AllEvaluatedIndividuals { get { return _allEvaluatedIndividuals; } }

        /// <summary>
        /// Ensures chromosome template lengths and rule markers are inferred from the population when not preset.
        /// </summary>
        private void EnsureTemplatesFromPopulation(List<GAIndividual> individuals)
        {
            if (individuals == null || individuals.Count == 0)
            {
                return;
            }

            if (_chromosomeTemplateLengths != null && _chromosomeTemplateLengths.Count > 0)
            {
                return;
            }

            List<int> chromo = individuals[0].Chromosome;
            List<int> lengths = new List<int>();
            List<int> markers = new List<int>();
            int i = 0;

            while (i < chromo.Count)
            {
                int ruleId = chromo[i];
                markers.Add(ruleId);
                i++;

                int len = 0;
                while (i < chromo.Count && chromo[i] != UT.RULE_END_MARKER && chromo[i] != -999)
                {
                    len++;
                    i++;
                }

                lengths.Add(len);
                i++; // skip end marker
            }

            _chromosomeTemplateLengths = lengths;
            _ruleMarkersTemplate = markers;
        }
    }
}