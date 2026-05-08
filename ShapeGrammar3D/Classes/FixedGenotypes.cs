using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Provides a deterministic, cached set of genotypes for controlled experiments.
    /// Uses a fixed seed so the same population is generated every run,
    /// allowing metric changes to be observed in isolation.
    /// </summary>
    public static class FixedGenotypes
    {
        private const int SEED = 42;
        /// <summary>Minimum pool size when a smaller population is requested (avoids tiny cache churn).</summary>
        private const int MIN_POOL_SIZE = 100;

        // Cache key: (chromosome structure hash) → cached pool
        private static List<GAIndividual> _cachedPool;
        private static int _cachedStructureHash;

        /// <summary>
        /// Returns a list of deterministic GAIndividuals.
        /// Rebuilds the cache when chromosome structure changes or when the pool is smaller than <paramref name="count"/>.
        /// </summary>
        /// <param name="count">Number of individuals to return (must be ≥ 1).</param>
        /// <param name="chromosomeLengths">Length of each chromosome segment (one per rule).</param>
        /// <param name="ruleMarkers">Rule marker IDs (one per rule).</param>
        /// <returns>Cloned list of deterministic individuals.</returns>
        public static List<GAIndividual> Get(int count, List<int> chromosomeLengths, List<int> ruleMarkers)
        {
            count = Math.Max(1, count);
            int structureHash = ComputeStructureHash(chromosomeLengths, ruleMarkers);
            int targetPool = Math.Max(count, MIN_POOL_SIZE);

            if (_cachedPool == null || _cachedStructureHash != structureHash || _cachedPool.Count < count)
            {
                _cachedPool = Generate(targetPool, chromosomeLengths, ruleMarkers);
                _cachedStructureHash = structureHash;
            }

            return _cachedPool.Take(count).Select(ind => ind.Clone()).ToList();
        }

        /// <summary>
        /// Forces regeneration of the cached pool on next request.
        /// </summary>
        public static void Reset()
        {
            _cachedPool = null;
            _cachedStructureHash = 0;
        }

        /// <summary>
        /// Generates a pool of deterministic individuals using a fixed seed.
        /// Mirrors the stratified activation logic from SG_GA.CreateInitialGeneration
        /// but with a seeded Random for full reproducibility.
        /// </summary>
        private static List<GAIndividual> Generate(int poolSize, List<int> chromosomeLengths, List<int> ruleMarkers)
        {
            var rng = new Random(SEED);
            var pool = new List<GAIndividual>(poolSize);

            for (int cnt = 0; cnt < poolSize; cnt++)
            {
                double t = (double)cnt / Math.Max(1, poolSize - 1);

                var chromosome = new List<int>();
                var chromosomeParam = new List<double>();

                for (int i = 0; i < chromosomeLengths.Count; i++)
                {
                    int len = chromosomeLengths[i];
                    int ruleId = ruleMarkers[i];

                    // Per-rule activation bias (same as SG_GA.CreateInitialGeneration)
                    double activationProb;
                    if (ruleId == UT.RULE011_MARKER || ruleId == UT.RULE020_MARKER)
                        activationProb = 0.75 + 0.10 * t;
                    else if (ruleId == UT.RULE041_MARKER || ruleId == UT.RULE051_MARKER)
                        activationProb = 0.70 + 0.20 * t;
                    else if (ruleId == UT.RULE010_MARKER)
                        activationProb = 0.40 + 0.30 * t;
                    else if (ruleId == UT.RULE031_MARKER || ruleId == UT.RULE032_MARKER)
                        activationProb = 0.45 + 0.40 * t;  // Rotation: 45%-85%
                    else
                        activationProb = 0.30 + 0.60 * t;

                    chromosome.Add(ruleId);
                    chromosomeParam.Add(ruleId);

                    for (int j = 0; j < len; j++)
                    {
                        chromosome.Add(rng.NextDouble() < activationProb ? 1 : 0);
                        chromosomeParam.Add(rng.NextDouble());
                    }

                    chromosome.Add(UT.RULE_END_MARKER);
                    chromosomeParam.Add(UT.RULE_END_MARKER);
                }

                pool.Add(new GAIndividual(chromosome, chromosomeParam,
                    string.Format("fixed_{0}", cnt)));
            }

            // Deterministic shuffle with the same seeded rng
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            return pool;
        }

        private static int ComputeStructureHash(List<int> lengths, List<int> markers)
        {
            unchecked
            {
                int hash = 17;
                foreach (int l in lengths)
                    hash = hash * 31 + l;
                foreach (int m in markers)
                    hash = hash * 31 + m;
                return hash;
            }
        }
    }
}