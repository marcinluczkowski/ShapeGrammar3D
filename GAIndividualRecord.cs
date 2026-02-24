using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Lightweight record: genotype + fitness + metadata only — no geometry.
    /// </summary>
    [Serializable]
    public class GAIndividualRecord
    {
        public string Id { get; set; }
        public int Generation { get; set; }
        public int ClusterGroup { get; set; }
        public List<int> Chromosome { get; set; }
        public List<double> ChromosomeParam { get; set; }
        public double Fitness { get; set; }
        public double Topo { get; set; }
        public double Shape { get; set; }

        public static GAIndividualRecord FromIndividual(GAIndividual ind, int generation)
        {
            return new GAIndividualRecord
            {
                Id = ind.Id,
                Generation = generation,
                ClusterGroup = ind.ClustGrp,
                Chromosome = new List<int>(ind.Chromosome),
                ChromosomeParam = new List<double>(ind.ChromosomeParam),
                Fitness = ind.Fitness,
                Topo = ind.Topo,
                Shape = ind.Shpe
            };
        }

        /// <summary>
        /// Reconstruct a full GAIndividual from this record.
        /// </summary>
        public GAIndividual ToIndividual()
        {
            var ind = new GAIndividual(Chromosome, ChromosomeParam, Id);
            ind.Fitness = Fitness;
            ind.Topo = Topo;
            ind.Shpe = Shape;
            ind.ClustGrp = ClusterGroup;
            return ind;
        }

        /// <summary>
        /// Build an SG_Genotype from the stored chromosome data.
        /// </summary>
        public SG_Genotype ToGenotype()
        {
            return new SG_Genotype(new List<int>(Chromosome), new List<double>(ChromosomeParam));
        }

        public override string ToString()
        {
            return string.Format("Record {0} | Gen {1} | Cluster {2} | Fit {3:F6} | Topo {4:F2} | Shape {5:F2}",
                Id, Generation, ClusterGroup, Fitness, Topo, Shape);
        }
    }

    /// <summary>
    /// Collects per-generation records and persists them to JSON.
    /// </summary>
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
        public List<GAIndividualRecord> Records { get; set; } = new List<GAIndividualRecord>();

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        /// <summary>
        /// Append one generation of evaluated individuals.
        /// </summary>
        public void AppendGeneration(List<GAIndividual> individuals, int generation)
        {
            foreach (var ind in individuals)
            {
                Records.Add(GAIndividualRecord.FromIndividual(ind, generation));
            }
        }

        /// <summary>
        /// Get all records for a given generation.
        /// </summary>
        public List<GAIndividualRecord> GetGeneration(int generation)
        {
            return Records.Where(r => r.Generation == generation).ToList();
        }

        /// <summary>
        /// Save the full run to a JSON file.
        /// </summary>
        public void SaveToJson(string filePath)
        {
            string json = JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Load a run from a JSON file.
        /// </summary>
        public static GARunStore LoadFromJson(string filePath)
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<GARunStore>(json, _jsonOptions);
        }
    }
}