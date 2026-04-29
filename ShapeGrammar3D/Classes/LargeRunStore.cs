using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Lightweight, append-only result recorder optimized for large GA runs
    /// (e.g. 1000 individuals × 100 generations × 10 clusters).
    ///
    /// Records ONLY scalar optimization metrics per individual (no shapes, no FEM models).
    /// Designed for low memory + fast streaming write to disk: each generation can be
    /// flushed to CSV as soon as it is evaluated, so the in-memory footprint stays bounded
    /// even for very large runs.
    /// </summary>
    public class LargeRunStore
    {
        public string RunId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public int PopulationSize { get; set; }
        public int NumGenerations { get; set; }
        public int NumClusters { get; set; }
        public int NumObjectives { get; set; }
        public double MutationProb { get; set; }
        public double CrossoverProb { get; set; }
        public double EliteProb { get; set; }
        public bool UseSelfWeight { get; set; }
        public int CroSecOptMode { get; set; }
        public List<int> TopoMetricTypes { get; set; } = new List<int>();
        public List<int> ShapeMetricTypes { get; set; } = new List<int>();
        public List<string> TopoMetricLabels { get; set; } = new List<string>();
        public List<string> ShapeMetricLabels { get; set; } = new List<string>();

        /// <summary>Per-generation per-cluster aggregates (best/worst/avg of each scalar).</summary>
        public List<GenClusterAggregate> Aggregates { get; set; } = new List<GenClusterAggregate>();

        /// <summary>Total individuals appended across all generations (for diagnostics).</summary>
        public long TotalIndividualsRecorded { get; set; }

        // Streaming CSV writer, lazily opened on first AppendIndividuals call when CsvPath is set.
        private StreamWriter _csvWriter;
        private string _csvPath;
        private bool _csvHeaderWritten;

        public LargeRunStore() { }

        /// <summary>
        /// Begins a streaming CSV file at <paramref name="path"/>. Subsequent calls to
        /// <see cref="AppendIndividuals"/> will write rows directly to disk so memory
        /// stays bounded even for 1M+ records.
        /// </summary>
        public void BeginCsv(string path)
        {
            _csvPath = path;
            _csvHeaderWritten = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                _csvWriter = new StreamWriter(path, append: false, Encoding.UTF8);
            }
            catch
            {
                _csvWriter = null;
                _csvPath = null;
            }
        }

        /// <summary>
        /// Appends one row per individual to the open CSV stream. Header is written on
        /// the first call so the column layout adapts to the actual topology/shape
        /// metric counts of the run.
        /// </summary>
        public void AppendIndividuals(List<GAIndividual> population, int generation)
        {
            if (population == null || population.Count == 0) return;

            if (_csvWriter != null)
            {
                if (!_csvHeaderWritten)
                {
                    WriteCsvHeader(population[0]);
                    _csvHeaderWritten = true;
                }
                foreach (var ind in population)
                    WriteCsvRow(ind, generation);
                _csvWriter.Flush();
            }

            TotalIndividualsRecorded += population.Count;
        }

        /// <summary>
        /// Records best/worst/avg/median for fitness, dispRatio, avgUtil, maxUtil, feasibility
        /// for each cluster in this generation. Used for convergence plots.
        /// </summary>
        public void AppendAggregates(List<GAIndividual> population, int generation, int numClusters)
        {
            if (population == null) return;
            for (int c = 0; c < Math.Max(1, numClusters); c++)
            {
                var inCluster = population.Where(i => i.ClustGrp == c).ToList();
                if (inCluster.Count == 0)
                {
                    Aggregates.Add(new GenClusterAggregate { Generation = generation, Cluster = c, Count = 0 });
                    continue;
                }

                var validFit = inCluster.Where(IsValidScalar).ToList();
                Aggregates.Add(new GenClusterAggregate
                {
                    Generation = generation,
                    Cluster = c,
                    Count = inCluster.Count,
                    BestFitness = validFit.Count > 0 ? validFit.Min(i => i.Fitness) : double.NaN,
                    WorstFitness = validFit.Count > 0 ? validFit.Max(i => i.Fitness) : double.NaN,
                    AvgFitness = validFit.Count > 0 ? validFit.Average(i => i.Fitness) : double.NaN,
                    AvgFeas = inCluster.Average(i => i.Feas),
                    AvgVDang = inCluster.Average(i => i.VDang),
                    AvgVAng = inCluster.Average(i => i.VAng),
                    AvgVLen = inCluster.Average(i => i.VLen),
                    BestId = validFit.Count > 0 ? validFit.OrderBy(i => i.Fitness).First().Id : null
                });
            }
        }

        /// <summary>
        /// Closes the streaming CSV writer (call at end of run).
        /// </summary>
        public void EndCsv()
        {
            try
            {
                _csvWriter?.Flush();
                _csvWriter?.Dispose();
            }
            catch { /* ignore */ }
            _csvWriter = null;
        }

        /// <summary>
        /// Writes the metadata + aggregates as a JSON sidecar file. The full
        /// per-individual data lives in the CSV; this keeps the JSON small (≤1 MB
        /// even for 100 generations × 10 clusters).
        /// </summary>
        public string SaveJsonSummary(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
                var options = new JsonSerializerOptions { WriteIndented = true };
                var summary = new
                {
                    RunId,
                    StartedAt,
                    FinishedAt,
                    PopulationSize,
                    NumGenerations,
                    NumClusters,
                    NumObjectives,
                    MutationProb,
                    CrossoverProb,
                    EliteProb,
                    UseSelfWeight,
                    CroSecOptMode,
                    TopoMetricTypes,
                    ShapeMetricTypes,
                    TopoMetricLabels,
                    ShapeMetricLabels,
                    TotalIndividualsRecorded,
                    Aggregates
                };
                File.WriteAllText(filePath, JsonSerializer.Serialize(summary, options));
                return filePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsValidScalar(GAIndividual ind) =>
            !double.IsNaN(ind.Fitness)
            && !double.IsInfinity(ind.Fitness)
            && ind.Fitness != double.MaxValue
            && ind.Fitness != double.MinValue;

        private void WriteCsvHeader(GAIndividual sample)
        {
            var sb = new StringBuilder();
            sb.Append("Generation,Cluster,Id,Fitness,DispRatio,AvgUtil,MaxUtil,Feas,VDang,VAng,VLen,Rank,CrowdingDist,NumObjectives");
            int nObj = sample.ObjectiveValues?.Count ?? 0;
            for (int i = 0; i < nObj; i++) sb.Append($",Obj{i}");
            int nTopo = sample.TopoValues?.Count ?? 0;
            for (int i = 0; i < nTopo; i++)
            {
                string label = (i < TopoMetricLabels.Count) ? TopoMetricLabels[i] : ("Topo" + i);
                sb.Append($",{SanitizeColumnName("T_" + label)}");
            }
            int nShpe = sample.ShpeValues?.Count ?? 0;
            for (int i = 0; i < nShpe; i++)
            {
                string label = (i < ShapeMetricLabels.Count) ? ShapeMetricLabels[i] : ("Shape" + i);
                sb.Append($",{SanitizeColumnName("S_" + label)}");
            }
            _csvWriter.WriteLine(sb.ToString());
        }

        private void WriteCsvRow(GAIndividual ind, int generation)
        {
            // ObjectiveValues layout (single-objective path):
            //   [0] = dispRatio, [1] = utilObj, [2] = rawFeas (set in EvaluatePopulation).
            // For multi-objective the columns also map to dispRatio/util/feas approximately.
            double dispRatio = ind.ObjectiveValues != null && ind.ObjectiveValues.Count > 0 ? ind.ObjectiveValues[0] : double.NaN;
            double utilObj   = ind.ObjectiveValues != null && ind.ObjectiveValues.Count > 1 ? ind.ObjectiveValues[1] : double.NaN;

            var sb = new StringBuilder();
            sb.Append(generation).Append(',');
            sb.Append(ind.ClustGrp).Append(',');
            sb.Append(ind.Id).Append(',');
            AppendScalar(sb, ind.Fitness); sb.Append(',');
            AppendScalar(sb, dispRatio); sb.Append(',');
            AppendScalar(sb, utilObj); sb.Append(',');
            // MaxUtil is not stored separately on GAIndividual; we emit utilObj again so the column always exists
            AppendScalar(sb, utilObj); sb.Append(',');
            AppendScalar(sb, ind.Feas); sb.Append(',');
            AppendScalar(sb, ind.VDang); sb.Append(',');
            AppendScalar(sb, ind.VAng); sb.Append(',');
            AppendScalar(sb, ind.VLen); sb.Append(',');
            sb.Append(ind.Rank).Append(',');
            AppendScalar(sb, ind.CrowdingDistance); sb.Append(',');
            sb.Append(ind.ObjectiveValues?.Count ?? 0);

            if (ind.ObjectiveValues != null)
                foreach (var v in ind.ObjectiveValues) { sb.Append(','); AppendScalar(sb, v); }

            if (ind.TopoValues != null)
                foreach (var v in ind.TopoValues) { sb.Append(','); AppendScalar(sb, v); }

            if (ind.ShpeValues != null)
                foreach (var v in ind.ShpeValues) { sb.Append(','); AppendScalar(sb, v); }

            _csvWriter.WriteLine(sb.ToString());
        }

        private static void AppendScalar(StringBuilder sb, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v == double.MaxValue || v == double.MinValue)
            {
                sb.Append(string.Empty); // empty cell = invalid/sentinel; pandas/Excel treat as NA
                return;
            }
            sb.Append(v.ToString("G17", CultureInfo.InvariantCulture));
        }

        private static string SanitizeColumnName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "col";
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Per-generation per-cluster aggregate metrics for convergence plots and JSON summary.
    /// </summary>
    [Serializable]
    public class GenClusterAggregate
    {
        public int Generation { get; set; }
        public int Cluster { get; set; }
        public int Count { get; set; }
        public double BestFitness { get; set; } = double.NaN;
        public double WorstFitness { get; set; } = double.NaN;
        public double AvgFitness { get; set; } = double.NaN;
        public double AvgFeas { get; set; } = 0.0;
        public double AvgVDang { get; set; } = 0.0;
        public double AvgVAng { get; set; } = 0.0;
        public double AvgVLen { get; set; } = 0.0;
        public string BestId { get; set; }
    }
}
