using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GrammarInterpreter_Settings : GH_Component
    {
        public GrammarInterpreter_Settings()
          : base("GrammarInterpreter_Settings", "GI_Settings",
              "Collects all GA/interpreter analysis settings into one object for Grammar Interpreter from SG_Shape (GI_FromSg).",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Population Size", "Pop",
                "GA population size. Number of individuals per generation.", GH_ParamAccess.item, 5);                              // 0
            pManager.AddIntegerParameter("Generations", "Gen",
                "Number of GA generations to run.", GH_ParamAccess.item, 3);                                                      // 1
            pManager.AddIntegerParameter("Clusters", "Clusters",
                "Number of clusters for KMeans diversity preservation.", GH_ParamAccess.item, 1);                                 // 2
            pManager.AddNumberParameter("Mutation Prob.", "Mut",
                "Mutation probability (0–1). Probability of mutating each gene.", GH_ParamAccess.item, 0.10);                    // 3
            pManager.AddNumberParameter("Crossover Prob.", "Cross",
                "Crossover probability (0–1). Probability of crossing two parents.", GH_ParamAccess.item, 0.9);                   // 4
            pManager.AddNumberParameter("Elite Prob.", "Elite",
                "Elite probability (0–1). Fraction of best individuals preserved each generation.", GH_ParamAccess.item, 0.1);    // 5

            pManager.AddNumberParameter("Topology Weight", "wTopo",
                "Weight for topology metrics in clustering. 0 = ignore topology in KMeans. KMeans uses NORMALIZED metrics.", GH_ParamAccess.item, 1.0);   // 6
            pManager.AddNumberParameter("Shape Weight", "wShpe",
                "Weight for shape metrics in clustering. 0 = ignore shape in KMeans. KMeans uses NORMALIZED metrics.", GH_ParamAccess.item, 1.0);         // 7
            pManager.AddNumberParameter("Fitness Weight", "wFit",
                "Weight for fitness (displacement) in clustering. 0 = ignore (default).", GH_ParamAccess.item, 0.0);              // 8
            pManager.AddIntegerParameter("KMeans Iterations", "KIter",
                "Max iterations for KMeans centroid updates per generation. Higher = more stable clusters.", GH_ParamAccess.item, 10);  // 9
            pManager.AddIntegerParameter("Recluster Interval", "ReClust",
                "Re-initialize KMeans centroids every N generations. 0 = only at generation 0.", GH_ParamAccess.item, 5);          // 10
            pManager.AddIntegerParameter("Topology Metrics", "TopoMet",
                "Topology metric selectors (supply one or many for n-dimensional clustering):\n" +
                "0=ElemCount, 1=NodeCount, 2=Elem/Node ratio, 3=AvgValence, 4=MaxValence, 5=LeafNodes, 6=BranchNodes, " +
                "7=Euler(V-E), 8=DistinctNames, 9=SupportCount, 10=ConnectedComponents(b₀), 11=CycleRank(b₁), " +
                "12=MaxPipeIntersections, 13=AvgPipeIntersections",
                GH_ParamAccess.list);                                                                                              // 11
            pManager.AddIntegerParameter("Shape Metrics", "ShpeMet",
                "Shape metric selectors (supply one or many for n-dimensional clustering):\n" +
                "0=TotalLength, 1=AvgLength, 2=MaxLength, 3=MinLength, 4=StdDevLength, 5=BBoxVolume, 6=BBoxDiagonal, " +
                "7=StructuralVolume, 8=MaxNodeSpan, 9=Compactness, 10=HullAreaXY, 11=HullAspectXY, 12=MeshArea(from lines), 13=Convex Hull Volume, 14=ShrinkWrap Volume",
                GH_ParamAccess.list);                                                                                              // 12
            pManager.AddNumberParameter("ShrinkWrap Detail", "ShDet",
                "Detail level for Shape Metric 14 (ShrinkWrap Volume) as bbox-diagonal ratio. Smaller = finer mesh, slower.",
                GH_ParamAccess.item, 0.02);                                                                                       // 12b
            pManager.AddBooleanParameter("Fixed Seed", "FixSeed",
                "Use a deterministic pre-generated population (same genotypes every run) for controlled metric comparison experiments.",
                GH_ParamAccess.item, false);                                                                                       // 13

            pManager.AddNumberParameter("Dangling Weight", "wDang",
                "Weight for dangling-bar feasibility penalty (0..1). Penalizes edges whose endpoint node has degree ≤ 1. " +
                "Applied as multiplicative fitness penalty: fit*(1+wDang*vDang). Set 0 to disable.",
                GH_ParamAccess.item, 0.20);                                                                                        // 14
            pManager.AddNumberParameter("Angle Weight", "wAng",
                "Weight for angle-based feasibility penalty (0..1). Penalizes very small angles (<10°), optimal >=30°.",
                GH_ParamAccess.item, 0.0);                                                                                         // 15
            pManager.AddNumberParameter("Length Weight", "wLen",
                "Weight for element length penalty (0..1). Uses Len bands below.",
                GH_ParamAccess.item, 0.0);                                                                                         // 16
            pManager.AddNumberParameter("Intersection Weight", "wInt",
                "Weight for bracing/column intersection penalty (0..1). Small factor recommended.",
                GH_ParamAccess.item, 0.0);                                                                                         // 17
            pManager.AddNumberParameter("Repet Weight", "wRepet",
                "Weight for repetitiveness penalty (0..1). Favours designs with similar element lengths (10% bins). Set 0 to disable.",
                GH_ParamAccess.item, 0.0);                                                                                         // 18
            pManager.AddNumberParameter("Duplicate Weight", "wDup",
                "Weight for duplicate-element penalty (0..1). Penalizes identical elements (Rule 051 can produce them). Goal: 0. Set 0 to disable.",
                GH_ParamAccess.item, 0.0);                                                                                         // 19
            pManager.AddNumberParameter("Angle Min (deg)", "AngMin",
                "Angle [deg]: below = full penalty; between Min and Opt = gradient to zero. Default 10.",
                GH_ParamAccess.item, 10.0);                                                                                        // 16c
            pManager.AddNumberParameter("Angle Opt (deg)", "AngOpt",
                "Angle [deg]: at or above = no penalty. Default 30.",
                GH_ParamAccess.item, 30.0);                                                                                       // 16d
            pManager.AddNumberParameter("Len Too Short (m)", "LenShort",
                "Length [m]: below = full penalty; to Len Opt Low = gradient to 0. Default 0.5.",
                GH_ParamAccess.item, 0.5);                                                                                        // 16e
            pManager.AddNumberParameter("Len Opt Low (m)", "LenOptLo",
                "Length [m]: start of good range (no penalty). Default 1.",
                GH_ParamAccess.item, 1.0);                                                                                        // 16f
            pManager.AddNumberParameter("Len Opt High (m)", "LenOptHi",
                "Length [m]: end of good range. Default 5.",
                GH_ParamAccess.item, 5.0);                                                                                       // 16g
            pManager.AddNumberParameter("Len Too Long (m)", "LenLong",
                "Length [m]: above = gradient then high penalty. Default 12.",
                GH_ParamAccess.item, 12.0);                                                                                      // 16h
            pManager.AddIntegerParameter("Num Objectives", "nObj",
                "Number of objectives: 1 = single-objective GA, " +
                "2 = bi-objective (displacement + util), " +
                "3 = tri-objective (+ feasibility). Multi-objective uses NSGA-II.",
                GH_ParamAccess.item, 1);                                                                                           // 17
            pManager.AddIntegerParameter("Single Obj Type", "SingleObj",
                "When nObj=1: which objective to minimize.\n" +
                "0 = Displacement (default)\n" +
                "1 = Feasibility\n" +
                "2 = Avg utilization deviation from 90%\n" +
                "3 = Max utilization",
                GH_ParamAccess.item, 0);                                                                                           // 17b
            pManager.AddIntegerParameter("Util Obj Type", "UtilObj",
                "When nObj>=2: which utilization metric for the util objective.\n" +
                "0 = Avg deviation from 90% (default)\n" +
                "1 = Max utilization (minimize highest element util)",
                GH_ParamAccess.item, 0);                                                                                           // 17c
            pManager.AddIntegerParameter("CroSec Opt", "CSOpt",
                "Cross-section optimization mode:\n" +
                "0 = off\n" +
                "1 = solid Rect (50–1000 mm, 50 mm steps)\n" +
                "2 = SHS catalog (square hollow sections)\n" +
                "3 = HEA/HEB catalog (European I-sections)\n" +
                "4 = Combined SHS + HEA/HEB\n" +
                "5 = RHS catalog (rectangular hollow sections)\n" +
                "6 = Combined SHS + RHS + HEA/HEB",
                GH_ParamAccess.item, 0);                                                                                           // 19
            pManager.AddIntervalParameter("Metric Domains", "MDom",
                "Expected [min, max] domain per metric dimension for clustering normalization.\n" +
                "Order: topology metrics first, then shape metrics (same order as MetNm output).\n" +
                "Each value is mapped to [0, 1] via (val - min) / (max - min).\n" +
                "If not supplied, falls back to observed-max normalization. KMeans uses NORMALIZED data.",
                GH_ParamAccess.list);                                                                                              // 20
            pManager.AddVectorParameter("Gravity Dir", "GDir",
                "Direction of gravity for self-weight loads. The vector is unitized internally; only direction matters.",
                GH_ParamAccess.item, new Vector3d(0, -1, 0));                                                                      // 21
            pManager.AddIntegerParameter("Cluster Elite", "ClElite",
                "Number of best individuals per cluster guaranteed to survive into the next generation. Prevents cluster extinction.\n" +
                "0 = disabled (default). Typical value: 1–3.",
                GH_ParamAccess.item, 0);                                                                                           // 22
            pManager.AddIntegerParameter("CSOpt Iterations", "CSIter",
                "Maximum FSD iterations for cross-section optimization. Higher = better convergence to 90% utilization but slower.\n" +
                "Default: 40. Typical range: 10–100.",
                GH_ParamAccess.item, 40);                                                                                          // 23

            pManager[11].Optional = true;
            pManager[12].Optional = true;
            pManager[31].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Param_GrammarInterpreterSettings(), "Settings", "Settings",
                "Interpreter settings object for GI_FromSg", GH_ParamAccess.item);                                                 // 0
            pManager.AddTextParameter("Info", "Info", "Settings summary", GH_ParamAccess.item);                                   // 1
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var settings = new GrammarInterpreterSettings();

            int populationSize = settings.PopulationSize;
            int generations = settings.Generations;
            int clusters = settings.Clusters;
            double mutationProb = settings.MutationProb;
            double crossoverProb = settings.CrossoverProb;
            double eliteProb = settings.EliteProb;
            double topologyWeight = settings.TopologyWeight;
            double shapeWeight = settings.ShapeWeight;
            double fitnessWeight = settings.FitnessWeight;
            int kmeansIterations = settings.KMeansIterations;
            int reclusterInterval = settings.ReclusterInterval;
            bool fixedSeed = settings.FixedSeed;
            double danglingWeight = settings.DanglingWeight;
            double angleWeight = settings.AngleWeight;
            double lengthWeight = settings.LengthWeight;
            double intersectionWeight = settings.IntersectionWeight;
            double repetWeight = settings.RepetWeight;
            double duplicateWeight = settings.DuplicateWeight;
            double angleMinDeg = settings.AngleMinDeg;
            double angleOptDeg = settings.AngleOptDeg;
            double lenTooShort = settings.LenTooShort;
            double lenOptLow = settings.LenOptLow;
            double lenOptHigh = settings.LenOptHigh;
            double lenTooLong = settings.LenTooLong;
            int numObjectives = settings.NumObjectives;
            int croSecOpt = settings.CroSecOpt;
            Vector3d gravityDir = settings.GravityDir;
            int clusterElite = settings.ClusterElite;
            int csOptIterations = settings.CSOptIterations;
            double shrinkWrapDetail = settings.ShapeShrinkWrapDetailRatio;

            DA.GetData(0, ref populationSize);
            DA.GetData(1, ref generations);
            DA.GetData(2, ref clusters);
            DA.GetData(3, ref mutationProb);
            DA.GetData(4, ref crossoverProb);
            DA.GetData(5, ref eliteProb);

            DA.GetData(6, ref topologyWeight);
            DA.GetData(7, ref shapeWeight);
            DA.GetData(8, ref fitnessWeight);
            DA.GetData(9, ref kmeansIterations);
            DA.GetData(10, ref reclusterInterval);

            var topo = new List<int>();
            if (DA.GetDataList(11, topo) && topo.Count > 0)
                settings.TopologyMetrics = topo;
            var shpe = new List<int>();
            if (DA.GetDataList(12, shpe) && shpe.Count > 0)
                settings.ShapeMetrics = shpe;
            DA.GetData(13, ref shrinkWrapDetail);
            DA.GetData(14, ref fixedSeed);

            DA.GetData(15, ref danglingWeight);
            DA.GetData(16, ref angleWeight);
            DA.GetData(17, ref lengthWeight);
            DA.GetData(18, ref intersectionWeight);
            DA.GetData(19, ref repetWeight);
            DA.GetData(20, ref duplicateWeight);
            DA.GetData(21, ref angleMinDeg);
            DA.GetData(22, ref angleOptDeg);
            DA.GetData(23, ref lenTooShort);
            DA.GetData(24, ref lenOptLow);
            DA.GetData(25, ref lenOptHigh);
            DA.GetData(26, ref lenTooLong);
            DA.GetData(27, ref numObjectives);
            int singleObjType = 0, utilObjType = 0;
            DA.GetData(28, ref singleObjType);
            DA.GetData(29, ref utilObjType);
            DA.GetData(30, ref croSecOpt);

            var rawDomains = new List<GH_Interval>();
            if (DA.GetDataList(31, rawDomains) && rawDomains.Count > 0)
                settings.MetricDomains = rawDomains.Select(d => d.Value).ToList();
            DA.GetData(32, ref gravityDir);
            DA.GetData(33, ref clusterElite);
            DA.GetData(34, ref csOptIterations);

            settings.PopulationSize = populationSize;
            settings.Generations = generations;
            settings.Clusters = clusters;
            settings.MutationProb = mutationProb;
            settings.CrossoverProb = crossoverProb;
            settings.EliteProb = eliteProb;
            settings.TopologyWeight = topologyWeight;
            settings.ShapeWeight = shapeWeight;
            settings.FitnessWeight = fitnessWeight;
            settings.KMeansIterations = kmeansIterations;
            settings.ReclusterInterval = reclusterInterval;
            settings.FixedSeed = fixedSeed;
            settings.ShapeShrinkWrapDetailRatio = shrinkWrapDetail;
            settings.DanglingWeight = danglingWeight;
            settings.AngleWeight = angleWeight;
            settings.LengthWeight = lengthWeight;
            settings.IntersectionWeight = intersectionWeight;
            settings.RepetWeight = repetWeight;
            settings.DuplicateWeight = duplicateWeight;
            settings.AngleMinDeg = angleMinDeg;
            settings.AngleOptDeg = angleOptDeg;
            settings.LenTooShort = lenTooShort;
            settings.LenOptLow = lenOptLow;
            settings.LenOptHigh = lenOptHigh;
            settings.LenTooLong = lenTooLong;
            settings.NumObjectives = numObjectives;
            settings.SingleObjType = singleObjType;
            settings.UtilObjType = utilObjType;
            settings.CroSecOpt = croSecOpt;
            settings.GravityDir = gravityDir;
            settings.ClusterElite = clusterElite;
            settings.CSOptIterations = csOptIterations;

            settings.Sanitize();

            DA.SetData(0, new GH_GrammarInterpreterSettings(settings));
            DA.SetData(1,
                $"Settings: Pop={settings.PopulationSize}, Gen={settings.Generations}, Clusters={settings.Clusters}, " +
                $"Obj={settings.NumObjectives}, CSOpt={settings.CroSecOpt}, KIter={settings.KMeansIterations}");
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("7A6B5C4D-3E2F-4A1B-9C8D-7E6F5A4B3C2D");
    }
}

