using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Stores lightweight GA run data for serialization to JSON.
    /// Records genotypes and fitness metrics without storing full shape/model data.
    /// </summary>
    [Serializable]
    public class GARunStore
    {
        public string RunId { get; set; }
        public DateTime StartedAt { get; set; }
        public int PopulationSize { get; set; }
        public int NumGenerations { get; set; }
        public int NumClusters { get; set; }
        public double MutationProb { get; set; }
        public double CrossoverProb { get; set; }
        public double EliteProb { get; set; }
        public List<GenerationRecord> Generations { get; set; }

        public GARunStore()
        {
            Generations = new List<GenerationRecord>();
        }

        /// <summary>
        /// Appends a generation's population data to the store
        /// </summary>
        public void AppendGeneration(List<GAIndividual> population, int generation)
        {
            var record = new GenerationRecord
            {
                Generation = generation,
                Individuals = population.Select(ind => new IndividualRecord
                {
                    Id = ind.Id,
                    Chromosome = new List<int>(ind.Chromosome),
                    ChromosomeParam = new List<double>(ind.ChromosomeParam),
                    Fitness = ind.Fitness,
                    Topo = ind.Topo,
                    Shpe = ind.Shpe,
                    ClustGrp = ind.ClustGrp
                }).ToList()
            };

            Generations.Add(record);
        }

        /// <summary>
        /// Saves the run data to a JSON file
        /// </summary>
        public void SaveToJson(string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to save GA run data to {filePath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads run data from a JSON file
        /// </summary>
        public static GARunStore LoadFromJson(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<GARunStore>(json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to load GA run data from {filePath}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Records data for a single generation
    /// </summary>
    [Serializable]
    public class GenerationRecord
    {
        public int Generation { get; set; }
        public List<IndividualRecord> Individuals { get; set; }
    }

    /// <summary>
    /// Lightweight record of an individual (genotype + fitness metrics only)
    /// </summary>
    [Serializable]
    public class IndividualRecord
    {
        public string Id { get; set; }
        public List<int> Chromosome { get; set; }
        public List<double> ChromosomeParam { get; set; }
        public double Fitness { get; set; }
        public double Topo { get; set; }
        public double Shpe { get; set; }
        public int ClustGrp { get; set; }
    }
}