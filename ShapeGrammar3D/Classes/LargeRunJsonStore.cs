using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Streaming writer for the GI_LargeSg "v2" JSON file. Memory footprint stays
    /// bounded (≈ one generation in flight at a time) so 500k+ individual runs are
    /// safe. The file contains everything needed to reload the run later:
    ///   * GA / interpreter settings + feasibility weights
    ///   * Initial SG_Shape fingerprint (so the reader can warn on mismatch)
    ///   * Rule manifest (chromosome lengths + markers + type names)
    ///   * Topology / shape metric labels + types
    ///   * Per-generation individual rows (id, cluster, fitness, objectives, chromosome, params)
    ///   * Per (generation, cluster) aggregates (best/worst/avg/feas) for convergence
    ///
    /// Per-individual model geometry is intentionally NOT stored - the reader
    /// rebuilds each model on demand by replaying the genotype against the
    /// supplied iniShape + rules using <see cref="StructuralEvaluator"/>.
    /// </summary>
    public sealed class LargeRunJsonStore : IDisposable
    {
        public const int FileVersion = 2;

        public string Path { get; private set; }
        public string RunId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public long TotalIndividualsRecorded { get; private set; }

        public List<GenClusterAggregate> Aggregates { get; } = new List<GenClusterAggregate>();

        private FileStream _fs;
        private Utf8JsonWriter _w;
        private bool _generationsArrayOpen;
        private bool _individualsArrayOpen;

        public LargeRunJsonStore() { }

        /// <summary>
        /// Opens <paramref name="path"/> for streaming, writes the run header,
        /// and starts the "generations" array. Subsequent calls must be
        /// <see cref="BeginGeneration"/> / <see cref="AppendIndividual"/> /
        /// <see cref="EndGeneration"/> followed by <see cref="Finish"/>.
        /// </summary>
        public void Begin(string path, RunHeader header)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
            if (header == null) throw new ArgumentNullException(nameof(header));

            Path = path;
            RunId = header.RunId;
            StartedAt = header.StartedAt == default ? DateTime.Now : header.StartedAt;

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
            _fs = File.Create(path);
            _w = new Utf8JsonWriter(_fs, new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = false
            });

            _w.WriteStartObject();
            _w.WriteNumber("version", FileVersion);
            _w.WriteString("runId", RunId ?? string.Empty);
            _w.WriteString("startedAt", StartedAt.ToString("o"));

            WriteSettings(header.Settings, header.Feasibility);
            WriteMetrics(header);
            WriteRulesManifest(header.Rules, header.ChromosomeLengths, header.RuleMarkers);
            WriteIniShapeFingerprint(header.IniShape);

            _w.WriteStartArray("generations");
            _generationsArrayOpen = true;
        }

        public void BeginGeneration(int generation)
        {
            EnsureWriter();
            if (!_generationsArrayOpen)
                throw new InvalidOperationException("Begin() must be called before BeginGeneration().");
            _w.WriteStartObject();
            _w.WriteNumber("g", generation);
            _w.WriteStartArray("ind");
            _individualsArrayOpen = true;
        }

        /// <summary>
        /// Appends one individual row. Chromosome and chromosome parameters are
        /// stored verbatim so the reader can recreate the exact same genotype.
        /// </summary>
        public void AppendIndividual(GAIndividual ind)
        {
            EnsureWriter();
            if (!_individualsArrayOpen)
                throw new InvalidOperationException("BeginGeneration() must be called before AppendIndividual().");
            if (ind == null) return;

            _w.WriteStartObject();
            _w.WriteString("id", ind.Id ?? string.Empty);
            _w.WriteNumber("c", ind.ClustGrp);
            WriteSafeNumber("f", ind.Fitness);
            _w.WriteNumber("rk", ind.Rank);
            WriteSafeNumber("cd", ind.CrowdingDistance);
            WriteSafeNumber("fe", ind.Feas);
            WriteSafeNumber("vd", ind.VDang);
            WriteSafeNumber("va", ind.VAng);
            WriteSafeNumber("vl", ind.VLen);
            WriteDoubleArray("o", ind.ObjectiveValues);
            WriteDoubleArray("t", ind.TopoValues);
            WriteDoubleArray("s", ind.ShpeValues);
            WriteIntArray("ch", ind.Chromosome);
            WriteDoubleArray("p", ind.ChromosomeParam);
            _w.WriteEndObject();

            TotalIndividualsRecorded++;
        }

        public void EndGeneration()
        {
            EnsureWriter();
            if (!_individualsArrayOpen) return;
            _w.WriteEndArray();
            _w.WriteEndObject();
            _individualsArrayOpen = false;
        }

        /// <summary>
        /// Computes per (generation, cluster) aggregate stats and stores them
        /// for the trailing "aggregates" array; written by <see cref="Finish"/>.
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
        /// Closes the generations array, writes aggregates and trailing
        /// metadata (totalIndividuals + finishedAt), then closes the document.
        /// </summary>
        public string Finish()
        {
            EnsureWriter();
            if (_individualsArrayOpen) EndGeneration();
            if (_generationsArrayOpen)
            {
                _w.WriteEndArray();
                _generationsArrayOpen = false;
            }

            _w.WriteStartArray("aggregates");
            foreach (var a in Aggregates.OrderBy(x => x.Generation).ThenBy(x => x.Cluster))
            {
                _w.WriteStartObject();
                _w.WriteNumber("g", a.Generation);
                _w.WriteNumber("c", a.Cluster);
                _w.WriteNumber("n", a.Count);
                WriteSafeNumber("best", a.BestFitness);
                WriteSafeNumber("worst", a.WorstFitness);
                WriteSafeNumber("avg", a.AvgFitness);
                WriteSafeNumber("avgFeas", a.AvgFeas);
                WriteSafeNumber("avgVDang", a.AvgVDang);
                WriteSafeNumber("avgVAng", a.AvgVAng);
                WriteSafeNumber("avgVLen", a.AvgVLen);
                _w.WriteString("bestId", a.BestId ?? string.Empty);
                _w.WriteEndObject();
            }
            _w.WriteEndArray();

            FinishedAt = DateTime.Now;
            _w.WriteString("finishedAt", FinishedAt.ToString("o"));
            _w.WriteNumber("totalIndividualsRecorded", TotalIndividualsRecorded);
            _w.WriteEndObject();
            _w.Flush();

            string outPath = Path;
            Dispose();
            return outPath;
        }

        public void Dispose()
        {
            try { _w?.Flush(); _w?.Dispose(); } catch { }
            try { _fs?.Dispose(); } catch { }
            _w = null;
            _fs = null;
            _generationsArrayOpen = false;
            _individualsArrayOpen = false;
        }

        // ─────── header helpers ───────

        private void WriteSettings(GrammarInterpreterSettings s, FeasibilitySettings feas)
        {
            _w.WriteStartObject("settings");
            if (s != null)
            {
                _w.WriteNumber("populationSize", s.PopulationSize);
                _w.WriteNumber("generations", s.Generations);
                _w.WriteNumber("clusters", s.Clusters);
                _w.WriteNumber("numObjectives", s.NumObjectives);
                _w.WriteNumber("singleObjType", s.SingleObjType);
                _w.WriteNumber("utilObjType", s.UtilObjType);
                _w.WriteNumber("mutationProb", s.MutationProb);
                _w.WriteNumber("crossoverProb", s.CrossoverProb);
                _w.WriteNumber("eliteProb", s.EliteProb);
                _w.WriteNumber("topologyWeight", s.TopologyWeight);
                _w.WriteNumber("shapeWeight", s.ShapeWeight);
                _w.WriteNumber("fitnessWeight", s.FitnessWeight);
                _w.WriteNumber("kMeansIterations", s.KMeansIterations);
                _w.WriteNumber("reclusterInterval", s.ReclusterInterval);
                _w.WriteNumber("clusterElite", s.ClusterElite);
                _w.WriteNumber("csOptIterations", s.CSOptIterations);
                _w.WriteNumber("croSecOpt", s.CroSecOpt);
                _w.WriteBoolean("useSelfWeight", s.SelfWeight);
                _w.WriteBoolean("fixedSeed", s.FixedSeed);
                _w.WriteNumber("shapeShrinkWrapDetailRatio", s.ShapeShrinkWrapDetailRatio);

                _w.WriteStartArray("topologyMetrics");
                if (s.TopologyMetrics != null) foreach (var v in s.TopologyMetrics) _w.WriteNumberValue(v);
                _w.WriteEndArray();

                _w.WriteStartArray("shapeMetrics");
                if (s.ShapeMetrics != null) foreach (var v in s.ShapeMetrics) _w.WriteNumberValue(v);
                _w.WriteEndArray();

                _w.WriteStartArray("metricDomains");
                if (s.MetricDomains != null)
                {
                    foreach (var iv in s.MetricDomains)
                    {
                        _w.WriteStartObject();
                        _w.WriteNumber("min", iv.T0);
                        _w.WriteNumber("max", iv.T1);
                        _w.WriteEndObject();
                    }
                }
                _w.WriteEndArray();

                _w.WriteStartArray("gravity");
                _w.WriteNumberValue(s.GravityDir.X);
                _w.WriteNumberValue(s.GravityDir.Y);
                _w.WriteNumberValue(s.GravityDir.Z);
                _w.WriteEndArray();
            }

            _w.WriteStartObject("feasibility");
            _w.WriteNumber("danglingWeight", feas.WDang);
            _w.WriteNumber("angleWeight", feas.WAng);
            _w.WriteNumber("lengthWeight", feas.WLen);
            _w.WriteNumber("intersectionWeight", feas.WIntersect);
            _w.WriteNumber("repetWeight", feas.WRepet);
            _w.WriteNumber("duplicateWeight", feas.WDup);
            _w.WriteNumber("angleMinDeg", feas.AngleMinDeg);
            _w.WriteNumber("angleOptDeg", feas.AngleOptDeg);
            _w.WriteNumber("lenTooShort", feas.LenTooShort);
            _w.WriteNumber("lenOptLow", feas.LenOptLow);
            _w.WriteNumber("lenOptHigh", feas.LenOptHigh);
            _w.WriteNumber("lenTooLong", feas.LenTooLong);
            _w.WriteEndObject();

            _w.WriteEndObject();
        }

        private void WriteMetrics(RunHeader h)
        {
            _w.WriteStartObject("metricLabels");
            _w.WriteStartArray("topology");
            if (h.TopoMetricLabels != null) foreach (var s in h.TopoMetricLabels) _w.WriteStringValue(s ?? string.Empty);
            _w.WriteEndArray();
            _w.WriteStartArray("shape");
            if (h.ShapeMetricLabels != null) foreach (var s in h.ShapeMetricLabels) _w.WriteStringValue(s ?? string.Empty);
            _w.WriteEndArray();
            _w.WriteEndObject();
        }

        private void WriteRulesManifest(List<SG_Rule> rules, List<int> chromosomeLengths, List<int> ruleMarkers)
        {
            _w.WriteStartArray("rules");
            int n = rules?.Count ?? 0;
            for (int i = 0; i < n; i++)
            {
                var r = rules[i];
                _w.WriteStartObject();
                _w.WriteString("type", r?.GetType().Name ?? string.Empty);
                _w.WriteString("name", r?.Name ?? string.Empty);
                _w.WriteNumber("marker", ruleMarkers != null && i < ruleMarkers.Count ? ruleMarkers[i] : (r?.RuleMarker ?? 0));
                _w.WriteNumber("chromosomeLength", chromosomeLengths != null && i < chromosomeLengths.Count ? chromosomeLengths[i] : 0);
                _w.WriteEndObject();
            }
            _w.WriteEndArray();
        }

        private void WriteIniShapeFingerprint(SG_Shape shape)
        {
            _w.WriteStartObject("iniShape");
            if (shape == null)
            {
                _w.WriteBoolean("present", false);
                _w.WriteEndObject();
                return;
            }

            _w.WriteBoolean("present", true);
            _w.WriteNumber("nodeCount", shape.Nodes?.Count ?? 0);
            _w.WriteNumber("elementCount", shape.Elems?.Count ?? 0);
            _w.WriteNumber("supportCount", shape.Supports?.Count ?? 0);
            _w.WriteNumber("pointLoadCount", shape.PointLoads?.Count ?? 0);
            _w.WriteNumber("lineLoadCount", shape.LineLoads?.Count ?? 0);
            _w.WriteBoolean("hasBoundaryBrep", shape.BoundaryBrep != null);
            _w.WriteBoolean("hasBoundaryMesh", shape.BoundaryMesh != null);

            _w.WriteStartArray("nodes");
            if (shape.Nodes != null)
            {
                foreach (var nd in shape.Nodes)
                {
                    if (nd == null) continue;
                    _w.WriteStartArray();
                    _w.WriteNumberValue(Math.Round(nd.Pt.X, 6));
                    _w.WriteNumberValue(Math.Round(nd.Pt.Y, 6));
                    _w.WriteNumberValue(Math.Round(nd.Pt.Z, 6));
                    _w.WriteEndArray();
                }
            }
            _w.WriteEndArray();

            _w.WriteStartArray("supports");
            if (shape.Supports != null)
            {
                foreach (var sup in shape.Supports)
                {
                    if (sup == null) continue;
                    _w.WriteStartObject();
                    _w.WriteNumber("cond", GetSupportCondition(sup));
                    _w.WriteStartArray("pt");
                    _w.WriteNumberValue(Math.Round(sup.Pt.X, 6));
                    _w.WriteNumberValue(Math.Round(sup.Pt.Y, 6));
                    _w.WriteNumberValue(Math.Round(sup.Pt.Z, 6));
                    _w.WriteEndArray();
                    _w.WriteEndObject();
                }
            }
            _w.WriteEndArray();

            _w.WriteStartArray("pointLoads");
            if (shape.PointLoads != null)
            {
                foreach (var pl in shape.PointLoads)
                {
                    if (pl == null) continue;
                    _w.WriteStartObject();
                    WriteVec(_w, "f", pl.Forces);
                    WriteVec(_w, "m", pl.Moments);
                    _w.WriteStartArray("pt");
                    _w.WriteNumberValue(Math.Round(pl.Position.X, 6));
                    _w.WriteNumberValue(Math.Round(pl.Position.Y, 6));
                    _w.WriteNumberValue(Math.Round(pl.Position.Z, 6));
                    _w.WriteEndArray();
                    _w.WriteEndObject();
                }
            }
            _w.WriteEndArray();

            _w.WriteEndObject();
        }

        private static int GetSupportCondition(SG_Support sup)
        {
            return sup?.SupportCondition ?? 0;
        }

        private static void WriteVec(Utf8JsonWriter w, string name, Vector3d v)
        {
            w.WriteStartArray(name);
            w.WriteNumberValue(v.X);
            w.WriteNumberValue(v.Y);
            w.WriteNumberValue(v.Z);
            w.WriteEndArray();
        }

        // ─────── value helpers ───────

        private void WriteSafeNumber(string name, double v)
        {
            _w.WritePropertyName(name);
            if (!IsValidScalar(v)) _w.WriteNullValue();
            else _w.WriteNumberValue(v);
        }

        private void WriteDoubleArray(string name, IList<double> values)
        {
            _w.WriteStartArray(name);
            if (values != null)
            {
                foreach (var v in values)
                {
                    if (!IsValidScalar(v)) _w.WriteNullValue();
                    else _w.WriteNumberValue(v);
                }
            }
            _w.WriteEndArray();
        }

        private void WriteIntArray(string name, IList<int> values)
        {
            _w.WriteStartArray(name);
            if (values != null)
                foreach (var v in values) _w.WriteNumberValue(v);
            _w.WriteEndArray();
        }

        private static bool IsValidScalar(double v)
        {
            return !double.IsNaN(v)
                && !double.IsInfinity(v)
                && v != double.MaxValue
                && v != double.MinValue;
        }

        private static bool IsValidScalar(GAIndividual ind) => IsValidScalar(ind.Fitness);

        private void EnsureWriter()
        {
            if (_w == null) throw new InvalidOperationException("LargeRunJsonStore not initialised; call Begin() first.");
        }
    }

    /// <summary>
    /// Header bundle passed to <see cref="LargeRunJsonStore.Begin"/>.
    /// </summary>
    public sealed class RunHeader
    {
        public string RunId { get; set; }
        public DateTime StartedAt { get; set; }
        public GrammarInterpreterSettings Settings { get; set; }
        public FeasibilitySettings Feasibility { get; set; }
        public List<SG_Rule> Rules { get; set; }
        public List<int> ChromosomeLengths { get; set; }
        public List<int> RuleMarkers { get; set; }
        public List<string> TopoMetricLabels { get; set; }
        public List<string> ShapeMetricLabels { get; set; }
        public SG_Shape IniShape { get; set; }
    }
}
