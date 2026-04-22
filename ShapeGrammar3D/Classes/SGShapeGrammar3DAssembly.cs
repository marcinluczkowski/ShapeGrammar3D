using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Toolbox;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// In-memory assembly storing full GA run data: genotypes, fitness, objectives,
    /// models and shapes for visualization (convergence, radar, deformation, Pareto).
    /// </summary>
    [Serializable]
    public class SGShapeGrammar3DAssembly
    {
        public AssemblyConfig Config { get; set; }
        public List<AssemblyGeneration> Generations { get; set; }
        public List<string> MetricNames { get; set; }

        public SGShapeGrammar3DAssembly()
        {
            Config = new AssemblyConfig();
            Generations = new List<AssemblyGeneration>();
            MetricNames = new List<string>();
        }

        /// <summary>Number of generations stored.</summary>
        public int GenerationCount => Generations?.Count ?? 0;

        /// <summary>Population size (from first generation if available).</summary>
        public int PopulationSize => (Generations?.Count > 0 && Generations[0].Individuals != null)
            ? Generations[0].Individuals.Count
            : 0;
    }

    [Serializable]
    public class AssemblyConfig
    {
        public int PopulationSize { get; set; }
        public int NumGenerations { get; set; }
        public int NumClusters { get; set; }
        public int NumObjectives { get; set; }
        public List<int> TopoMetricTypes { get; set; }
        public List<int> ShapeMetricTypes { get; set; }

        /// <summary>Feasibility angle/length limits from the interpreter (GI_FromSg). Used by GI_Feasibility Preview when set.</summary>
        public double? FeasibilityAngleMinDeg { get; set; }
        public double? FeasibilityAngleOptDeg { get; set; }
        public double? FeasibilityLenTooShort { get; set; }
        public double? FeasibilityLenOptLow { get; set; }
        public double? FeasibilityLenOptHigh { get; set; }
        public double? FeasibilityLenTooLong { get; set; }

        public AssemblyConfig()
        {
            TopoMetricTypes = new List<int>();
            ShapeMetricTypes = new List<int>();
        }
    }

    [Serializable]
    public class AssemblyGeneration
    {
        public int Generation { get; set; }
        public List<AssemblyIndividual> Individuals { get; set; }

        public AssemblyGeneration()
        {
            Individuals = new List<AssemblyIndividual>();
        }
    }

    [Serializable]
    public class AssemblyIndividual
    {
        public string Id { get; set; }
        public List<int> Chromosome { get; set; }
        public List<double> ChromosomeParam { get; set; }
        public double Fitness { get; set; }
        public List<double> ObjectiveValues { get; set; }
        public int Rank { get; set; }
        public double CrowdingDistance { get; set; }
        public int ClustGrp { get; set; }
        public List<double> TopoValues { get; set; }
        public List<double> ShpeValues { get; set; }
        public double Feas { get; set; }
        public double VDang { get; set; }
        public double VAng { get; set; }
        public double VLen { get; set; }
        public TB_Model Model { get; set; }
        public SG_Shape Shape { get; set; }

        public AssemblyIndividual()
        {
            Chromosome = new List<int>();
            ChromosomeParam = new List<double>();
            ObjectiveValues = new List<double>();
            TopoValues = new List<double>();
            ShpeValues = new List<double>();
        }

        public static AssemblyIndividual FromGAIndividual(GAIndividual ind, TB_Model model, SG_Shape shape)
        {
            var a = new AssemblyIndividual
            {
                Id = ind.Id,
                Chromosome = ind.Chromosome != null ? new List<int>(ind.Chromosome) : new List<int>(),
                ChromosomeParam = ind.ChromosomeParam != null ? new List<double>(ind.ChromosomeParam) : new List<double>(),
                Fitness = ind.Fitness,
                ObjectiveValues = ind.ObjectiveValues != null ? new List<double>(ind.ObjectiveValues) : new List<double>(),
                Rank = ind.Rank,
                CrowdingDistance = ind.CrowdingDistance,
                ClustGrp = ind.ClustGrp,
                TopoValues = ind.TopoValues != null ? new List<double>(ind.TopoValues) : new List<double>(),
                ShpeValues = ind.ShpeValues != null ? new List<double>(ind.ShpeValues) : new List<double>(),
                Feas = ind.Feas,
                VDang = ind.VDang,
                VAng = ind.VAng,
                VLen = ind.VLen,
                Model = model?.DeepCopy(),
                Shape = shape?.DeepCopy()
            };
            return a;
        }

        public double ObjUtil => ObjectiveValues != null && ObjectiveValues.Count > 1 ? ObjectiveValues[1] : 0.0;
        public double ObjFeas => ObjectiveValues != null && ObjectiveValues.Count > 2 ? ObjectiveValues[2] : 0.0;

        public List<double> AllMetrics()
        {
            var list = new List<double>();
            if (TopoValues != null) list.AddRange(TopoValues);
            if (ShpeValues != null) list.AddRange(ShpeValues);
            if (Fitness >= 0 && Fitness < double.MaxValue * 0.5) list.Add(Fitness);
            return list;
        }
    }

    public class GH_SGAssembly : GH_Goo<SGShapeGrammar3DAssembly>
    {
        public GH_SGAssembly() { }
        public GH_SGAssembly(SGShapeGrammar3DAssembly a) : base(a) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "SG Assembly";
        public override string TypeDescription => "Shape Grammar 3D in-memory GA run assembly";

        public override IGH_Goo Duplicate()
        {
            return new GH_SGAssembly(Value);
        }

        public override string ToString()
        {
            return Value != null
                ? string.Format("SG Assembly ({0} gen, {1} ind)", Value.GenerationCount, Value.PopulationSize)
                : "SG Assembly (null)";
        }
    }

    public class Param_SGAssembly : GH_PersistentParam<GH_SGAssembly>
    {
        public Param_SGAssembly() : base(
            new GH_InstanceDescription(
                "SG Assembly", "Assembly",
                "In-memory Shape Grammar 3D GA run (genotypes, results, models)",
                UT.CAT, UT.GR_DATA_PREVIEW))
        { }

        public override Guid ComponentGuid => new Guid("E8A1B2C3-4D5E-6F7A-8B9C-0D1E2F3A4B5C");
        protected override System.Drawing.Bitmap Icon => null;

        protected override GH_GetterResult Prompt_Plural(ref List<GH_SGAssembly> values) => GH_GetterResult.success;
        protected override GH_GetterResult Prompt_Singular(ref GH_SGAssembly value) => GH_GetterResult.success;
    }
}
