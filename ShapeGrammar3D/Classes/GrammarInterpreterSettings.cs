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

        public bool FixedSeed { get; set; } = false;

        public double DanglingWeight { get; set; } = 0.20;
        public double AngleWeight { get; set; } = 0.0;
        public double LengthWeight { get; set; } = 0.0;

        public int NumObjectives { get; set; } = 1;
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
            KMeansIterations = Math.Max(1, KMeansIterations);
            ReclusterInterval = Math.Max(0, ReclusterInterval);
            TopologyMetrics = (TopologyMetrics == null || TopologyMetrics.Count == 0) ? new List<int> { 0 } : TopologyMetrics.Distinct().ToList();
            ShapeMetrics = (ShapeMetrics == null || ShapeMetrics.Count == 0) ? new List<int> { 0 } : ShapeMetrics.Distinct().ToList();

            DanglingWeight = Math.Clamp(DanglingWeight, 0.0, 1.0);
            AngleWeight = Math.Clamp(AngleWeight, 0.0, 1.0);
            LengthWeight = Math.Clamp(LengthWeight, 0.0, 1.0);

            NumObjectives = Math.Clamp(NumObjectives, 1, 3);
            CroSecOpt = Math.Clamp(CroSecOpt, 0, 4);
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
                FixedSeed = Value.FixedSeed,
                DanglingWeight = Value.DanglingWeight,
                AngleWeight = Value.AngleWeight,
                LengthWeight = Value.LengthWeight,
                NumObjectives = Value.NumObjectives,
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
                "Grammar Interpreter settings bundle",
                UT.CAT, UT.GR_INT))
        { }

        public override Guid ComponentGuid => new Guid("9E8D7C6B-5A4F-4321-9B8A-7C6D5E4F3A21");
        protected override System.Drawing.Bitmap Icon => null;
        protected override GH_GetterResult Prompt_Plural(ref List<GH_GrammarInterpreterSettings> values) => GH_GetterResult.success;
        protected override GH_GetterResult Prompt_Singular(ref GH_GrammarInterpreterSettings value) => GH_GetterResult.success;
    }
}

