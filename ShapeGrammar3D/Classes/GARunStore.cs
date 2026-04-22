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
        public int NumObjectives { get; set; }
        public bool UseSelfWeight { get; set; }
        public bool UseCroSecOpt { get; set; }
        public List<int> TopoMetricTypes { get; set; }
        public List<int> ShapeMetricTypes { get; set; }
        public List<GenerationRecord> Generations { get; set; }

        public GARunStore()
        {
            Generations = new List<GenerationRecord>();
            TopoMetricTypes = new List<int>();
            ShapeMetricTypes = new List<int>();
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
                    ChromosomeParam = SanitizeList(ind.ChromosomeParam),
                    Fitness = SanitizeDouble(ind.Fitness),
                    Topo = SanitizeDouble(ind.Topo),
                    Shpe = SanitizeDouble(ind.Shpe),
                    TopoValues = SanitizeList(ind.TopoValues),
                    ShpeValues = SanitizeList(ind.ShpeValues),
                    ClustGrp = ind.ClustGrp,
                    Feas = SanitizeDouble(ind.Feas),
                    VDang = SanitizeDouble(ind.VDang),
                    VAng = SanitizeDouble(ind.VAng),
                    VLen = SanitizeDouble(ind.VLen),
                    Rank = ind.Rank,
                    CrowdingDistance = SanitizeDouble(ind.CrowdingDistance),
                    ObjectiveValues = SanitizeList(ind.ObjectiveValues)
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

        private static double SanitizeDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return double.MaxValue;
            return value;
        }

        private static List<double> SanitizeList(List<double> values)
        {
            if (values == null) return new List<double>();
            return values.Select(SanitizeDouble).ToList();
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
        public List<double> TopoValues { get; set; }
        public List<double> ShpeValues { get; set; }
        public int ClustGrp { get; set; }
        public double Feas { get; set; }
        public double VDang { get; set; }
        public double VAng { get; set; }
        public double VLen { get; set; }
        public int Rank { get; set; }
        public double CrowdingDistance { get; set; }
        public List<double> ObjectiveValues { get; set; }

        public IndividualRecord()
        {
            TopoValues = new List<double>();
            ShpeValues = new List<double>();
            ObjectiveValues = new List<double>();
        }
    }
}