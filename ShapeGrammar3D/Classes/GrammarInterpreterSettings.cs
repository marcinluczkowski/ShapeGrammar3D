using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class GrammarInterpreterSettings
    {
        public int PopulationSize { get; set; } = 5;
        public int Generations { get; set; } = 3;
        public int Clusters { get; set; } = 1;
        public double MutationProb { get; set; } = 0.10;
        public double CrossoverProb { get; set; } = 0.9;
        public double EliteProb { get; set; } = 0.1;

        public double TopologyWeight { get; set; } = 1.0;
        public double ShapeWeight { get; set; } = 1.0;
        public double FitnessWeight { get; set; } = 0.0;
        public int KMeansIterations { get; set; } = 10;
        public int ReclusterInterval { get; set; } = 5;
        public List<int> TopologyMetrics { get; set; } = new List<int> { 0 };
        public List<int> ShapeMetrics { get; set; } = new List<int> { 0 };
        public double ShapeShrinkWrapDetailRatio { get; set; } = 0.02;

        public bool FixedSeed { get; set; } = false;

        public double DanglingWeight { get; set; } = 0.20;
        public double AngleWeight { get; set; } = 0.0;
        public double LengthWeight { get; set; } = 0.0;
        public double IntersectionWeight { get; set; } = 0.0;
        public double RepetWeight { get; set; } = 0.0;
        public double DuplicateWeight { get; set; } = 0.0;

        /// <summary>Angle [deg] below which full penalty. 10–20° gradient to zero at AngleOptDeg.</summary>
        public double AngleMinDeg { get; set; } = 10.0;
        public double AngleOptDeg { get; set; } = 30.0;
        /// <summary>Length [m]: below = full penalty; to LenOptLow = gradient to 0.</summary>
        public double LenTooShort { get; set; } = 0.5;
        public double LenOptLow { get; set; } = 1.0;
        public double LenOptHigh { get; set; } = 5.0;
        public double LenTooLong { get; set; } = 12.0;

        public int NumObjectives { get; set; } = 1;
        /// <summary>When nObj=1: 0=Disp, 1=Feasibility, 2=AvgUtilDev, 3=MaxUtil.</summary>
        public int SingleObjType { get; set; } = 0;
        /// <summary>When nObj>=2: 0=Avg utilization deviation from 90%, 1=Max utilization.</summary>
        public int UtilObjType { get; set; } = 0;
        public bool SelfWeight { get; set; } = false;
        public int CroSecOpt { get; set; } = 0;
        public List<Interval> MetricDomains { get; set; } = new List<Interval>();
        public Vector3d GravityDir { get; set; } = new Vector3d(0, -1, 0);
        public int ClusterElite { get; set; } = 0;
        public int CSOptIterations { get; set; } = 40;

        public GrammarInterpreterSettings Sanitize()
        {
            PopulationSize = Math.Max(1, PopulationSize);
            Generations = Math.Max(1, Generations);
            Clusters = Math.Max(1, Clusters);
            MutationProb = Math.Clamp(MutationProb, 0.0, 1.0);
            CrossoverProb = Math.Clamp(CrossoverProb, 0.0, 1.0);
            EliteProb = Math.Clamp(EliteProb, 0.0, 1.0);

            TopologyWeight = Math.Max(0.0, TopologyWeight);
            ShapeWeight = Math.Max(0.0, ShapeWeight);
            FitnessWeight = Math.Max(0.0, FitnessWeight);
            ShapeShrinkWrapDetailRatio = Math.Clamp(ShapeShrinkWrapDetailRatio, 0.001, 0.2);
            KMeansIterations = Math.Max(1, KMeansIterations);
            ReclusterInterval = Math.Max(0, ReclusterInterval);
            TopologyMetrics = (TopologyMetrics == null || TopologyMetrics.Count == 0) ? new List<int> { 0 } : TopologyMetrics.Distinct().ToList();
            ShapeMetrics = (ShapeMetrics == null || ShapeMetrics.Count == 0) ? new List<int> { 0 } : ShapeMetrics.Distinct().ToList();

            DanglingWeight = Math.Clamp(DanglingWeight, 0.0, 1.0);
            AngleWeight = Math.Clamp(AngleWeight, 0.0, 1.0);
            LengthWeight = Math.Clamp(LengthWeight, 0.0, 1.0);
            IntersectionWeight = Math.Clamp(IntersectionWeight, 0.0, 1.0);
            RepetWeight = Math.Clamp(RepetWeight, 0.0, 1.0);
            DuplicateWeight = Math.Clamp(DuplicateWeight, 0.0, 1.0);
            AngleMinDeg = Math.Clamp(AngleMinDeg, 0.0, 90.0);
            AngleOptDeg = Math.Clamp(AngleOptDeg, 0.0, 180.0);
            if (AngleOptDeg <= AngleMinDeg) AngleOptDeg = AngleMinDeg + 5.0;
            LenTooShort = Math.Max(0.01, LenTooShort);
            LenOptLow = Math.Max(LenTooShort, LenOptLow);
            LenOptHigh = Math.Max(LenOptLow, LenOptHigh);
            LenTooLong = Math.Max(LenOptHigh, LenTooLong);

            NumObjectives = Math.Clamp(NumObjectives, 1, 3);
            SingleObjType = Math.Clamp(SingleObjType, 0, 3);
            UtilObjType = Math.Clamp(UtilObjType, 0, 1);
            CroSecOpt = Math.Clamp(CroSecOpt, 0, 6);
            ClusterElite = Math.Max(0, ClusterElite);
            CSOptIterations = Math.Clamp(CSOptIterations, 1, 500);

            if (GravityDir.Length > 1e-12) GravityDir.Unitize();
            else GravityDir = new Vector3d(0, -1, 0);

            return this;
        }
    }

    public class GH_GrammarInterpreterSettings : GH_Goo<GrammarInterpreterSettings>
    {
        public GH_GrammarInterpreterSettings() { }
        public GH_GrammarInterpreterSettings(GrammarInterpreterSettings settings) : base(settings) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "GI Settings";
        public override string TypeDescription => "Grammar Interpreter settings";
        public override IGH_Goo Duplicate()
        {
            return new GH_GrammarInterpreterSettings(Value == null ? null : new GrammarInterpreterSettings
            {
                PopulationSize = Value.PopulationSize,
                Generations = Value.Generations,
                Clusters = Value.Clusters,
                MutationProb = Value.MutationProb,
                CrossoverProb = Value.CrossoverProb,
                EliteProb = Value.EliteProb,
                TopologyWeight = Value.TopologyWeight,
                ShapeWeight = Value.ShapeWeight,
                FitnessWeight = Value.FitnessWeight,
                KMeansIterations = Value.KMeansIterations,
                ReclusterInterval = Value.ReclusterInterval,
                TopologyMetrics = Value.TopologyMetrics != null ? new List<int>(Value.TopologyMetrics) : new List<int>(),
                ShapeMetrics = Value.ShapeMetrics != null ? new List<int>(Value.ShapeMetrics) : new List<int>(),
                ShapeShrinkWrapDetailRatio = Value.ShapeShrinkWrapDetailRatio,
                FixedSeed = Value.FixedSeed,
                DanglingWeight = Value.DanglingWeight,
                AngleWeight = Value.AngleWeight,
                LengthWeight = Value.LengthWeight,
                IntersectionWeight = Value.IntersectionWeight,
                RepetWeight = Value.RepetWeight,
                DuplicateWeight = Value.DuplicateWeight,
                AngleMinDeg = Value.AngleMinDeg,
                AngleOptDeg = Value.AngleOptDeg,
                LenTooShort = Value.LenTooShort,
                LenOptLow = Value.LenOptLow,
                LenOptHigh = Value.LenOptHigh,
                LenTooLong = Value.LenTooLong,
                NumObjectives = Value.NumObjectives,
                SingleObjType = Value.SingleObjType,
                UtilObjType = Value.UtilObjType,
                SelfWeight = Value.SelfWeight,
                CroSecOpt = Value.CroSecOpt,
                MetricDomains = Value.MetricDomains != null ? new List<Interval>(Value.MetricDomains) : new List<Interval>(),
                GravityDir = Value.GravityDir,
                ClusterElite = Value.ClusterElite,
                CSOptIterations = Value.CSOptIterations
            });
        }
        public override string ToString()
        {
            return Value == null
                ? "GI Settings (null)"
                : $"GI Settings (Pop={Value.PopulationSize}, Gen={Value.Generations}, Obj={Value.NumObjectives})";
        }
    }

    public class Param_GrammarInterpreterSettings : GH_PersistentParam<GH_GrammarInterpreterSettings>
    {
        public Param_GrammarInterpreterSettings() : base(
            new GH_InstanceDescription(
                "GI Settings", "Settings",
                "Bundle of GA / interpreter options (population, generations, objectives, feasibility weights, cross-section optimisation, etc.).",
                UT.CAT, UT.GR_INT))
        { }

        public override Guid ComponentGuid => new Guid("9E8D7C6B-5A4F-4321-9B8A-7C6D5E4F3A21");
        protected override System.Drawing.Bitmap Icon => global::ShapeGrammar3D.Properties.Resources.icons_Generic;
        protected override GH_GetterResult Prompt_Plural(ref List<GH_GrammarInterpreterSettings> values) => GH_GetterResult.success;
        protected override GH_GetterResult Prompt_Singular(ref GH_GrammarInterpreterSettings value) => GH_GetterResult.success;
    }
}

