using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Holds aggregated results from a GA run (e.g. GI_Auto7): per generation, per cluster,
    /// best/worst/average for each objective (displacement, utilization, feasibility).
    /// Used for organized output and downstream components (reader, Pareto).
    /// </summary>
    [Serializable]
    public class ResultAssembly
    {
        public int PopulationSize { get; set; }
        public int NumGenerations { get; set; }
        public int NumClusters { get; set; }
        public int NumObjectives { get; set; }

        /// <summary>Per-generation results. Index = generation (0..NumGenerations-1).</summary>
        public List<GenerationResult> Generations { get; set; } = new List<GenerationResult>();

        public ResultAssembly()
        {
        }

        /// <summary>Add or update one generation's aggregated metrics per cluster.</summary>
        public void SetGeneration(int generationIndex, List<ClusterResult> clusterResults)
        {
            while (Generations.Count <= generationIndex)
                Generations.Add(new GenerationResult { Generation = Generations.Count });
            Generations[generationIndex].Generation = generationIndex;
            Generations[generationIndex].Clusters = clusterResults ?? new List<ClusterResult>();
        }
    }

    [Serializable]
    public class GenerationResult
    {
        public int Generation { get; set; }
        /// <summary>One entry per cluster (index = cluster id).</summary>
        public List<ClusterResult> Clusters { get; set; } = new List<ClusterResult>();
    }

    [Serializable]
    public class ClusterResult
    {
        public int Cluster { get; set; }

        /// <summary>Objective 0: displacement (best = minimum for minimization).</summary>
        public double DispBest { get; set; }
        public double DispWorst { get; set; }
        public double DispAvg { get; set; }

        /// <summary>Objective 1: utilization (avg deviation or max util depending on run).</summary>
        public double UtilBest { get; set; }
        public double UtilWorst { get; set; }
        public double UtilAvg { get; set; }

        /// <summary>Objective 2: feasibility (lower is better).</summary>
        public double FeasBest { get; set; }
        public double FeasWorst { get; set; }
        public double FeasAvg { get; set; }

        /// <summary>Combined fitness best/worst/avg (single-objective or composite).</summary>
        public double FitnessBest { get; set; }
        public double FitnessWorst { get; set; }
        public double FitnessAvg { get; set; }
    }

    public class GH_ResultAssembly : GH_Goo<ResultAssembly>
    {
        public GH_ResultAssembly() { }
        public GH_ResultAssembly(ResultAssembly a) : base(a) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "Result Assembly";
        public override string TypeDescription => "GA run results: per-generation, per-cluster metrics (displacement, utilization, feasibility)";

        public override IGH_Goo Duplicate() => new GH_ResultAssembly(Value);

        public override string ToString()
        {
            return Value != null
                ? string.Format("Result Assembly ({0} gen, {1} clusters)", Value.NumGenerations, Value.NumClusters)
                : "Result Assembly (null)";
        }
    }

    public class Param_ResultAssembly : Grasshopper.Kernel.GH_PersistentParam<GH_ResultAssembly>
    {
        public Param_ResultAssembly() : base(
            new Grasshopper.Kernel.GH_InstanceDescription(
                "Result Assembly", "Results",
                "GA run results from GI_Auto7: best/worst/avg per objective per cluster per generation",
                UT.CAT, UT.GR_DATA_PREVIEW))
        { }

        public override Guid ComponentGuid => new Guid("F9B2C3D4-5E6F-7A8B-9C0D-1E2F3A4B5C6D");
        protected override System.Drawing.Bitmap Icon => null;

        protected override Grasshopper.Kernel.GH_GetterResult Prompt_Plural(ref List<GH_ResultAssembly> values) => Grasshopper.Kernel.GH_GetterResult.success;
        protected override Grasshopper.Kernel.GH_GetterResult Prompt_Singular(ref GH_ResultAssembly value) => Grasshopper.Kernel.GH_GetterResult.success;
    }
}
