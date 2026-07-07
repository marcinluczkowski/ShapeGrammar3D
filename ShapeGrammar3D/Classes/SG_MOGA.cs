using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Multi-Objective Genetic Algorithm using NSGA-II.
    /// Supports 2 or 3 objectives with non-dominated sorting,
    /// crowding distance, and Pareto-based selection.
    /// All objectives are minimized.
    /// </summary>
    [Serializable]
    public class SG_MOGA
    {
        // ── Configuration ────────────────────────────────────────────────
        public int PopulationSize { get; set; } = 100;
        public int NumGenerations { get; set; } = 50;
        public double MutationProbability { get; set; } = 0.10;
        public double CrossoverProbability { get; set; } = 0.9;
        public double EliteProbability { get; set; } = 0.1;
        public double BlxAlpha { get; set; } = 0.3;
        public int NumObjectives { get; set; } = 2;

        // ── State ────────────────────────────────────────────────────────
        private int _currentGeneration;
        private List<GAIndividual> _previousParents;
        private List<int> _chromosomeTemplateLengths = new List<int>();
        private List<int> _ruleMarkersTemplate = new List<int>();

        public int CurrentGeneration => _currentGeneration;
        public void IncrementGeneration() => _currentGeneration++;

        public SG_MOGA()
        {
            _currentGeneration = 0;
            _previousParents = null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Initial population (mirrors SG_GA logic with stratified activation)
        // ══════════════════════════════════════════════════════════════════

        public List<GAIndividual> CreateInitialGeneration(
            int populationCount,
            List<int> chromosomeLengths,
            List<int> ruleMarkers)
        {
            if (chromosomeLengths == null) throw new ArgumentNullException(nameof(chromosomeLengths));
            if (ruleMarkers == null) throw new ArgumentNullException(nameof(ruleMarkers));
            if (chromosomeLengths.Count != ruleMarkers.Count)
                throw new ArgumentException("chromosomeLengths and ruleMarkers must have the same count.");

            _chromosomeTemplateLengths = new List<int>(chromosomeLengths);
            _ruleMarkersTemplate = new List<int>(ruleMarkers);

            var generation = new List<GAIndividual>();

            for (int cnt = 0; cnt < populationCount; cnt++)
            {
                double t = (double)cnt / Math.Max(1, populationCount - 1);

                var chromosome = new List<int>();
                var chromosomeParam = new List<double>();

                for (int i = 0; i < chromosomeLengths.Count; i++)
                {
                    int len = chromosomeLengths[i];
                    int ruleId = ruleMarkers[i];

                    double activationProb = GetActivationProbability(ruleId, t);

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

            for (int i = generation.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (generation[i], generation[j]) = (generation[j], generation[i]);
            }

            return generation;
        }

        private static double GetActivationProbability(int ruleId, double t)
        {
            if (ruleId == UT.RULE011_MARKER || ruleId == UT.RULE020_MARKER)
                return 0.75 + 0.10 * t;
            if (ruleId == UT.RULE041_MARKER || ruleId == UT.RULE051_MARKER)
                return 0.70 + 0.20 * t;
            if (ruleId == UT.RULE010_MARKER)
                return 0.40 + 0.30 * t;
            if (ruleId == UT.RULE031_MARKER || ruleId == UT.RULE032_MARKER)
                return 0.45 + 0.40 * t;  // Rotation: 45%-85%
            return 0.30 + 0.60 * t;
        }

        // ══════════════════════════════════════════════════════════════════
        //  NSGA-II main loop
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Processes evaluated individuals using NSGA-II.
        /// On the first call, the evaluated population is sorted and offspring are generated.
        /// On subsequent calls, previously stored parents are combined with the newly
        /// evaluated offspring (2N pool), then the best N are selected by non-domination
        /// rank and crowding distance, and a new N offspring are generated.
        /// </summary>
        public List<GAIndividual> ProcessEvaluatedIndividuals(List<GAIndividual> evaluated)
        {
            List<GAIndividual> combined;
            if (_previousParents != null && _previousParents.Count > 0)
            {
                combined = new List<GAIndividual>(_previousParents.Count + evaluated.Count);
                combined.AddRange(_previousParents);
                combined.AddRange(evaluated);
            }
            else
            {
                combined = new List<GAIndividual>(evaluated);
            }

            var fronts = NonDominatedSort(combined);
            foreach (var front in fronts)
                CalculateCrowdingDistance(front);

            List<GAIndividual> nextParents = SelectNextGeneration(fronts, PopulationSize);
            _previousParents = nextParents.Select(i => i.Clone()).ToList();

            List<GAIndividual> offspring = GenerateOffspring(nextParents);
            return offspring;
        }

        /// <summary>
        /// Returns the current Pareto-optimal parent archive (set after ProcessEvaluatedIndividuals).
        /// Useful for outputting final Pareto front.
        /// </summary>
        public List<GAIndividual> GetCurrentParents()
        {
            return _previousParents ?? new List<GAIndividual>();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Non-dominated sorting
        // ══════════════════════════════════════════════════════════════════

        public List<List<GAIndividual>> NonDominatedSort(List<GAIndividual> population)
        {
            int n = population.Count;
            int[] dominationCount = new int[n];
            var dominated = new List<int>[n];

            for (int i = 0; i < n; i++)
                dominated[i] = new List<int>();

            var firstFront = new List<int>();

            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    int cmp = CompareDominance(population[p], population[q]);
                    if (cmp == 1)
                    {
                        dominated[p].Add(q);
                        dominationCount[q]++;
                    }
                    else if (cmp == -1)
                    {
                        dominated[q].Add(p);
                        dominationCount[p]++;
                    }
                }

                if (dominationCount[p] == 0)
                {
                    population[p].Rank = 0;
                    firstFront.Add(p);
                }
            }

            var indexFronts = new List<List<int>> { firstFront };
            int fi = 0;

            while (indexFronts[fi].Count > 0)
            {
                var nextFront = new List<int>();
                foreach (int p in indexFronts[fi])
                {
                    foreach (int q in dominated[p])
                    {
                        dominationCount[q]--;
                        if (dominationCount[q] == 0)
                        {
                            population[q].Rank = fi + 1;
                            nextFront.Add(q);
                        }
                    }
                }
                if (nextFront.Count == 0) break;
                indexFronts.Add(nextFront);
                fi++;
            }

            var result = new List<List<GAIndividual>>();
            foreach (var front in indexFronts)
                result.Add(front.Select(i => population[i]).ToList());
            return result;
        }

        /// <summary>
        /// Returns 1 if a dominates b, -1 if b dominates a, 0 if neither (all minimized).
        /// </summary>
        private int CompareDominance(GAIndividual a, GAIndividual b)
        {
            if (a.ObjectiveValues.Count == 0 || b.ObjectiveValues.Count == 0)
                return 0;

            bool aBetter = false;
            bool bBetter = false;
            int numObj = Math.Min(Math.Min(a.ObjectiveValues.Count, b.ObjectiveValues.Count), NumObjectives);

            for (int i = 0; i < numObj; i++)
            {
                double va = ClampObjective(a.ObjectiveValues[i]);
                double vb = ClampObjective(b.ObjectiveValues[i]);
                if (va < vb) aBetter = true;
                else if (vb < va) bBetter = true;
            }

            if (aBetter && !bBetter) return 1;
            if (bBetter && !aBetter) return -1;
            return 0;
        }

        private static double ClampObjective(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v == double.MaxValue)
                return 1e18;
            if (v == double.MinValue)
                return -1e18;
            return v;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Crowding distance
        // ══════════════════════════════════════════════════════════════════

        public void CalculateCrowdingDistance(List<GAIndividual> front)
        {
            int n = front.Count;
            if (n <= 2)
            {
                foreach (var ind in front)
                    ind.CrowdingDistance = double.MaxValue;
                return;
            }

            foreach (var ind in front)
                ind.CrowdingDistance = 0.0;

            int numObj = NumObjectives;
            if (front[0].ObjectiveValues.Count < numObj)
                numObj = front[0].ObjectiveValues.Count;

            for (int m = 0; m < numObj; m++)
            {
                var sorted = front.OrderBy(ind => ClampObjective(ind.ObjectiveValues[m])).ToList();

                double fMin = ClampObjective(sorted[0].ObjectiveValues[m]);
                double fMax = ClampObjective(sorted[n - 1].ObjectiveValues[m]);
                double range = fMax - fMin;

                sorted[0].CrowdingDistance = double.MaxValue;
                sorted[n - 1].CrowdingDistance = double.MaxValue;

                if (range > 0)
                {
                    for (int i = 1; i < n - 1; i++)
                    {
                        if (sorted[i].CrowdingDistance < double.MaxValue)
                        {
                            sorted[i].CrowdingDistance +=
                                (ClampObjective(sorted[i + 1].ObjectiveValues[m])
                                 - ClampObjective(sorted[i - 1].ObjectiveValues[m]))
                                / range;
                        }
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  NSGA-II environmental selection (fill next generation front-by-front)
        // ══════════════════════════════════════════════════════════════════

        private List<GAIndividual> SelectNextGeneration(
            List<List<GAIndividual>> fronts, int targetSize)
        {
            var selected = new List<GAIndividual>();

            foreach (var front in fronts)
            {
                if (selected.Count + front.Count <= targetSize)
                {
                    selected.AddRange(front.Select(i => i.Clone()));
                }
                else
                {
                    int remaining = targetSize - selected.Count;
                    var sortedByCrowding = front
                        .OrderByDescending(i => i.CrowdingDistance)
                        .ToList();
                    selected.AddRange(sortedByCrowding.Take(remaining).Select(i => i.Clone()));
                    break;
                }
            }

            return selected;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Offspring generation
        // ══════════════════════════════════════════════════════════════════

        private List<GAIndividual> GenerateOffspring(List<GAIndividual> parents)
        {
            var offspring = new List<GAIndividual>();
            int targetSize = parents.Count;

            int numPairs = (int)(targetSize * CrossoverProbability) / 2;
            for (int i = 0; i < numPairs; i++)
            {
                GAIndividual p1 = TournamentSelect(parents);
                GAIndividual p2 = TournamentSelect(parents);

                TwoPointCrossover(p1, p2, out GAIndividual child1, out GAIndividual child2);
                CrossoverBlxAlpha(p1, p2, out List<double> paramChild1, out List<double> paramChild2);

                child1.ChromosomeParam = AlignChromosomeParams(child1, paramChild1);
                child2.ChromosomeParam = AlignChromosomeParams(child2, paramChild2);

                offspring.Add(child1);
                offspring.Add(child2);
            }

            EnsureTemplatesFromPopulation(parents);
            int numMutated = (int)(targetSize * MutationProbability);
            if (numMutated > 0 && _chromosomeTemplateLengths.Count > 0)
            {
                var mutated = CreateInitialGeneration(numMutated, _chromosomeTemplateLengths, _ruleMarkersTemplate);
                offspring.AddRange(mutated);
            }

            while (offspring.Count < targetSize)
                offspring.Add(TournamentSelect(parents).Clone());

            if (offspring.Count > targetSize)
                offspring = offspring.Take(targetSize).ToList();

            return offspring;
        }

        /// <summary>
        /// Binary tournament: prefer lower rank, then higher crowding distance.
        /// </summary>
        private GAIndividual TournamentSelect(List<GAIndividual> population)
        {
            int sampleSize = Math.Min(3, population.Count);
            var tournament = population
                .OrderBy(_ => Random.Shared.Next())
                .Take(sampleSize)
                .ToList();

            return tournament
                .OrderBy(i => i.Rank)
                .ThenByDescending(i => i.CrowdingDistance)
                .First();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Genetic operators (same as SG_GA)
        // ══════════════════════════════════════════════════════════════════

        private void TwoPointCrossover(
            GAIndividual parent1, GAIndividual parent2,
            out GAIndividual child1, out GAIndividual child2)
        {
            int size = parent1.Chromosome.Count;
            if (size < 2)
            {
                child1 = parent1.Clone();
                child2 = parent2.Clone();
                return;
            }

            var chromo1 = new List<int>(parent1.Chromosome);
            var chromo2 = new List<int>(parent2.Chromosome);

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

        private void CrossoverBlxAlpha(
            GAIndividual parent1, GAIndividual parent2,
            out List<double> child1, out List<double> child2)
        {
            var p1 = new List<double>(parent1.ChromosomeParam);
            var p2 = new List<double>(parent2.ChromosomeParam);

            child1 = new List<double>();
            child2 = new List<double>();

            if (p1.Count == 0) return;

            var invalidIndices = new List<int>();
            var invalidValues = new List<double>();

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

                child1.Add(Clamp(gene1, 0.0, 1.0));
                child2.Add(Clamp(gene2, 0.0, 1.0));
            }

            for (int i = 0; i < invalidIndices.Count; i++)
            {
                child1.Insert(invalidIndices[i], invalidValues[i]);
                child2.Insert(invalidIndices[i], invalidValues[i]);
            }
        }

        private List<double> AlignChromosomeParams(GAIndividual owner, List<double> candidateParams)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            int length = owner.Chromosome.Count;
            var result = new List<double>(length);

            for (int i = 0; i < length; i++)
            {
                double value = (candidateParams != null && i < candidateParams.Count)
                    ? candidateParams[i]
                    : 0.5;

                if (owner.Chromosome[i] < 0)
                    value = owner.Chromosome[i];
                else
                    value = Clamp(value, 0.0, 1.0);

                result.Add(value);
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Utilities
        // ══════════════════════════════════════════════════════════════════

        private void EnsureTemplatesFromPopulation(List<GAIndividual> individuals)
        {
            if (individuals == null || individuals.Count == 0) return;
            if (_chromosomeTemplateLengths != null && _chromosomeTemplateLengths.Count > 0) return;

            List<int> chromo = individuals[0].Chromosome;
            var lengths = new List<int>();
            var markers = new List<int>();
            int i = 0;

            while (i < chromo.Count)
            {
                markers.Add(chromo[i]);
                i++;
                int len = 0;
                while (i < chromo.Count && chromo[i] != UT.RULE_END_MARKER && chromo[i] != -999)
                {
                    len++;
                    i++;
                }
                lengths.Add(len);
                i++;
            }

            _chromosomeTemplateLengths = lengths;
            _ruleMarkersTemplate = markers;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
