using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Pre-parsed, preview-ready snapshot of a large GA run. <see cref="Components.GrammarInterpreter_LargeJsonReader"/>
    /// parses the (potentially huge) JSON once and packs the result here so downstream
    /// preview components can re-render instantly when graph parameters change, without
    /// re-reading and re-parsing the file.
    ///
    /// Holds plain data only (no Rhino-heavy geometry). The preview component turns this
    /// into points, lines and labels.
    /// </summary>
    public class OptimizationResults
    {
        public string RunId { get; set; }
        public string JsonPath { get; set; }
        public int Version { get; set; }

        public int PopulationSize { get; set; }
        public int NumGenerations { get; set; }
        public int NumClusters { get; set; }
        public int NumObjectives { get; set; }

        public List<string> TopoLabels { get; set; } = new List<string>();
        public List<string> ShapeLabels { get; set; } = new List<string>();

        /// <summary>Per (generation, cluster) convergence aggregates.</summary>
        public List<OptAggregate> Aggregates { get; set; } = new List<OptAggregate>();

        /// <summary>Individual data points (already filtered by the reader).</summary>
        public List<OptPoint> Points { get; set; } = new List<OptPoint>();

        public OptimizationResults() { }

        /// <summary>Distinct cluster ids present in <see cref="Points"/>, ascending.</summary>
        public List<int> ClusterIds()
        {
            var set = new SortedSet<int>();
            foreach (var p in Points) set.Add(p.Cluster);
            if (set.Count == 0) set.Add(0);
            return set.ToList();
        }

        /// <summary>Distinct generation indices present in <see cref="Points"/>, ascending.</summary>
        public List<int> GenerationIds()
        {
            var set = new SortedSet<int>();
            foreach (var p in Points) set.Add(p.Generation);
            return set.ToList();
        }

        /// <summary>Upper bound on cluster count (max id + 1) used for colour mapping.</summary>
        public int ClusterSpan()
        {
            int span = NumClusters;
            foreach (var p in Points) if (p.Cluster + 1 > span) span = p.Cluster + 1;
            return Math.Max(1, span);
        }
    }

    /// <summary>Convergence aggregate for one generation/cluster pair.</summary>
    public class OptAggregate
    {
        public int Generation { get; set; }
        public int Cluster { get; set; }
        public int Count { get; set; }
        public double Best { get; set; }
        public double Worst { get; set; }
        public double Avg { get; set; }
    }

    /// <summary>One evaluated individual, reduced to what previews need.</summary>
    public class OptPoint
    {
        public int Generation { get; set; }
        public int IndexInGen { get; set; }
        public int Cluster { get; set; }
        public int Rank { get; set; }
        public string Id { get; set; }
        public double Fitness { get; set; }
        public double Feas { get; set; }
        public List<double> Objectives { get; set; } = new List<double>();

        /// <summary>Raw Pareto coordinate: X = displacement ratio, Y = utilisation objective,
        /// Z = third objective (or feasibility).</summary>
        public Point3d Raw { get; set; }
    }

    public class GH_OptimizationResults : GH_Goo<OptimizationResults>
    {
        public GH_OptimizationResults() { }
        public GH_OptimizationResults(OptimizationResults r) : base(r) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "Optimization Results";
        public override string TypeDescription => "Pre-parsed large GA run (aggregates + data points) for fast preview";

        public override IGH_Goo Duplicate() => new GH_OptimizationResults(Value);

        public override string ToString()
        {
            if (Value == null) return "Optimization Results (null)";
            return string.Format("Optimization Results (run {0}, {1} pts, {2} clusters, {3} obj)",
                string.IsNullOrEmpty(Value.RunId) ? "-" : Value.RunId,
                Value.Points?.Count ?? 0,
                Value.NumClusters,
                Value.NumObjectives);
        }
    }

    public class Param_OptimizationResults : GH_PersistentParam<GH_OptimizationResults>
    {
        public Param_OptimizationResults() : base(
            new GH_InstanceDescription(
                "Optimization Results", "OptRes",
                "Pre-parsed large GA run emitted by GI_LargeJson Reader. Feed into GI_Opti Preview to render Pareto/convergence without re-reading the JSON.",
                UT.CAT, UT.GR_PARAM))
        { }

        public override Guid ComponentGuid => new Guid("3F1C8A6E-2D4B-4F7A-9C1E-7B5A0D9E2C13");
        protected override System.Drawing.Bitmap Icon => global::ShapeGrammar3D.Properties.Resources.icons_P_OptResults;

        protected override GH_GetterResult Prompt_Plural(ref List<GH_OptimizationResults> values) => GH_GetterResult.success;
        protected override GH_GetterResult Prompt_Singular(ref GH_OptimizationResults value) => GH_GetterResult.success;
    }
}
