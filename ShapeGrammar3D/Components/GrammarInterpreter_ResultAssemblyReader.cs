using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Reads a Result Assembly from GI_Auto7. Exposes per-generation, per-cluster metrics
    /// (displacement, utilization, feasibility best/worst/avg) and optional Pareto-style points for visualization.
    /// </summary>
    public class GI_ResultAssemblyReader : GH_Component
    {
        public GI_ResultAssemblyReader()
          : base("GI_ResultAssemblyReader", "GI_ResRead",
              "Read Result Assembly from GI_Auto7: metrics per generation per cluster (disp/util/feas). Optional Pareto-style points.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_ResultAssembly(), "Result Assembly", "Results",
                "Result Assembly from GI_Auto7", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Pareto Plane", "Pln",
                "Plane for Pareto-style point layout (origin = graph origin)", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Pareto W", "PW", "Pareto graph width (X)", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Pareto H", "PH", "Pareto graph height (Y)", GH_ParamAccess.item, 6.0);
            pManager.AddNumberParameter("Pareto D", "PD", "Pareto graph depth (Z, 3-objective)", GH_ParamAccess.item, 6.0);
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generations", "Gen", "Generation indices", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Clusters", "Clust", "Cluster indices", GH_ParamAccess.list);
            pManager.AddNumberParameter("Disp Best", "DBest", "Displacement best. Path = cluster; branch = list of values in generation order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Disp Worst", "DWorst", "Displacement worst. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Disp Avg", "DAvg", "Displacement average. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Util Best", "UBest", "Utilization best. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Util Worst", "UWorst", "Utilization worst. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Util Avg", "UAvg", "Utilization average. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Feas Best", "FBest", "Feasibility best. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Feas Worst", "FWorst", "Feasibility worst. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Feas Avg", "FAvg", "Feasibility average. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Fitness Best", "FitBest", "Fitness best. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Fitness Worst", "FitWorst", "Fitness worst. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Fitness Avg", "FitAvg", "Fitness average. Path = cluster; branch = list in gen order.", GH_ParamAccess.tree);
            pManager.AddPointParameter("Pareto Pts", "Pts", "Pareto-style points: one per (gen, cluster). 3D = (Disp, Util, Feas) normalised. Path = (gen, cluster).", GH_ParamAccess.tree);
            pManager.AddColourParameter("Pareto Colours", "PCol", "Cluster colour per point", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ResultAssembly ghResults = null;
            if (!DA.GetData(0, ref ghResults) || ghResults?.Value == null)
            {
                DA.SetData(0, "No Result Assembly.");
                return;
            }
            var results = ghResults.Value;

            Plane paretoPlane = Plane.WorldXY;
            double pW = 10.0, pH = 6.0, pD = 6.0;
            DA.GetData(1, ref paretoPlane);
            DA.GetData(2, ref pW);
            DA.GetData(3, ref pH);
            DA.GetData(4, ref pD);

            var gens = results.Generations ?? new List<GenerationResult>();
            var genIndices = gens.Select(g => g.Generation).OrderBy(x => x).ToList();
            int numClusters = results.NumClusters;
            var clusterIndices = Enumerable.Range(0, numClusters).ToList();

            // Per-cluster: build list of values in generation order (one branch per cluster, list = gen 0, gen 1, ...)
            var dispBestByCluster = new Dictionary<int, List<double>>();
            var dispWorstByCluster = new Dictionary<int, List<double>>();
            var dispAvgByCluster = new Dictionary<int, List<double>>();
            var utilBestByCluster = new Dictionary<int, List<double>>();
            var utilWorstByCluster = new Dictionary<int, List<double>>();
            var utilAvgByCluster = new Dictionary<int, List<double>>();
            var feasBestByCluster = new Dictionary<int, List<double>>();
            var feasWorstByCluster = new Dictionary<int, List<double>>();
            var feasAvgByCluster = new Dictionary<int, List<double>>();
            var fitBestByCluster = new Dictionary<int, List<double>>();
            var fitWorstByCluster = new Dictionary<int, List<double>>();
            var fitAvgByCluster = new Dictionary<int, List<double>>();

            foreach (var gen in gens.OrderBy(g => g.Generation))
            {
                foreach (var c in gen.Clusters ?? new List<ClusterResult>())
                {
                    int ci = c.Cluster;
                    if (!dispBestByCluster.ContainsKey(ci)) { dispBestByCluster[ci] = new List<double>(); dispWorstByCluster[ci] = new List<double>(); dispAvgByCluster[ci] = new List<double>(); utilBestByCluster[ci] = new List<double>(); utilWorstByCluster[ci] = new List<double>(); utilAvgByCluster[ci] = new List<double>(); feasBestByCluster[ci] = new List<double>(); feasWorstByCluster[ci] = new List<double>(); feasAvgByCluster[ci] = new List<double>(); fitBestByCluster[ci] = new List<double>(); fitWorstByCluster[ci] = new List<double>(); fitAvgByCluster[ci] = new List<double>(); }
                    dispBestByCluster[ci].Add(c.DispBest);
                    dispWorstByCluster[ci].Add(c.DispWorst);
                    dispAvgByCluster[ci].Add(c.DispAvg);
                    utilBestByCluster[ci].Add(c.UtilBest);
                    utilWorstByCluster[ci].Add(c.UtilWorst);
                    utilAvgByCluster[ci].Add(c.UtilAvg);
                    feasBestByCluster[ci].Add(c.FeasBest);
                    feasWorstByCluster[ci].Add(c.FeasWorst);
                    feasAvgByCluster[ci].Add(c.FeasAvg);
                    fitBestByCluster[ci].Add(c.FitnessBest);
                    fitWorstByCluster[ci].Add(c.FitnessWorst);
                    fitAvgByCluster[ci].Add(c.FitnessAvg);
                }
            }

            var dispBest = new GH_Structure<GH_Number>();
            var dispWorst = new GH_Structure<GH_Number>();
            var dispAvg = new GH_Structure<GH_Number>();
            var utilBest = new GH_Structure<GH_Number>();
            var utilWorst = new GH_Structure<GH_Number>();
            var utilAvg = new GH_Structure<GH_Number>();
            var feasBest = new GH_Structure<GH_Number>();
            var feasWorst = new GH_Structure<GH_Number>();
            var feasAvg = new GH_Structure<GH_Number>();
            var fitBest = new GH_Structure<GH_Number>();
            var fitWorst = new GH_Structure<GH_Number>();
            var fitAvg = new GH_Structure<GH_Number>();

            foreach (int ci in dispBestByCluster.Keys.OrderBy(k => k))
            {
                GH_Path path = new GH_Path(ci);
                foreach (double v in dispBestByCluster[ci]) dispBest.Append(new GH_Number(v), path);
                foreach (double v in dispWorstByCluster[ci]) dispWorst.Append(new GH_Number(v), path);
                foreach (double v in dispAvgByCluster[ci]) dispAvg.Append(new GH_Number(v), path);
                foreach (double v in utilBestByCluster[ci]) utilBest.Append(new GH_Number(v), path);
                foreach (double v in utilWorstByCluster[ci]) utilWorst.Append(new GH_Number(v), path);
                foreach (double v in utilAvgByCluster[ci]) utilAvg.Append(new GH_Number(v), path);
                foreach (double v in feasBestByCluster[ci]) feasBest.Append(new GH_Number(v), path);
                foreach (double v in feasWorstByCluster[ci]) feasWorst.Append(new GH_Number(v), path);
                foreach (double v in feasAvgByCluster[ci]) feasAvg.Append(new GH_Number(v), path);
                foreach (double v in fitBestByCluster[ci]) fitBest.Append(new GH_Number(v), path);
                foreach (double v in fitWorstByCluster[ci]) fitWorst.Append(new GH_Number(v), path);
                foreach (double v in fitAvgByCluster[ci]) fitAvg.Append(new GH_Number(v), path);
            }

            var allD = dispBestByCluster.Values.SelectMany(l => l).Where(v => !double.IsNaN(v)).ToList();
            var allU = utilBestByCluster.Values.SelectMany(l => l).Where(v => !double.IsNaN(v)).ToList();
            var allF = feasBestByCluster.Values.SelectMany(l => l).Where(v => !double.IsNaN(v)).ToList();
            double dMin = allD.Count > 0 ? allD.Min() : 0;
            double dMax = allD.Count > 0 ? allD.Max() : 1;
            double uMin = allU.Count > 0 ? allU.Min() : 0;
            double uMax = allU.Count > 0 ? allU.Max() : 1;
            double fMin = allF.Count > 0 ? allF.Min() : 0;
            double fMax = allF.Count > 0 ? allF.Max() : 1;
            if (dMax <= dMin) dMax = dMin + 1.0;
            if (uMax <= uMin) uMax = uMin + 1.0;
            if (fMax <= fMin) fMax = fMin + 1.0;
            double dR = dMax - dMin, uR = uMax - uMin, fR = fMax - fMin;

            var paretoPts = new GH_Structure<GH_Point>();
            var paretoCols = new GH_Structure<GH_Colour>();
            foreach (var gen in gens)
            {
                foreach (var c in gen.Clusters ?? new List<ClusterResult>())
                {
                    GH_Path ptPath = new GH_Path(gen.Generation, c.Cluster);
                    double dx = double.IsNaN(c.DispBest) ? 0.5 : Math.Clamp((c.DispBest - dMin) / dR, 0, 1);
                    double ux = double.IsNaN(c.UtilBest) ? 0.5 : Math.Clamp((c.UtilBest - uMin) / uR, 0, 1);
                    double fx = double.IsNaN(c.FeasBest) ? 0.5 : Math.Clamp((c.FeasBest - fMin) / fR, 0, 1);
                    Point3d pt = paretoPlane.Origin + paretoPlane.XAxis * (dx * pW) + paretoPlane.YAxis * (ux * pH) + paretoPlane.ZAxis * (fx * pD);
                    paretoPts.Append(new GH_Point(pt), ptPath);
                    paretoCols.Append(new GH_Colour(GetClusterColour(c.Cluster, numClusters)), ptPath);
                }
            }

            DA.SetData(0, string.Format(
                "Result Assembly: {0} gen, {1} clusters, pop {2}. Objectives: Disp, Util, Feas (best/worst/avg per cluster per gen).",
                results.NumGenerations, results.NumClusters, results.PopulationSize));
            DA.SetDataList(1, genIndices);
            DA.SetDataList(2, clusterIndices);
            DA.SetDataTree(3, dispBest);
            DA.SetDataTree(4, dispWorst);
            DA.SetDataTree(5, dispAvg);
            DA.SetDataTree(6, utilBest);
            DA.SetDataTree(7, utilWorst);
            DA.SetDataTree(8, utilAvg);
            DA.SetDataTree(9, feasBest);
            DA.SetDataTree(10, feasWorst);
            DA.SetDataTree(11, feasAvg);
            DA.SetDataTree(12, fitBest);
            DA.SetDataTree(13, fitWorst);
            DA.SetDataTree(14, fitAvg);
            DA.SetDataTree(15, paretoPts);
            DA.SetDataTree(16, paretoCols);
        }

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0, 1);
            int r = 0, g, b;
            if (t <= 0.5) { double s = t / 0.5; g = (int)(s * 255); b = 255; }
            else { double s = (t - 0.5) / 0.5; g = 255; b = (int)((1.0 - s) * 255); }
            return Color.FromArgb(r, g, b);
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;
        public override Guid ComponentGuid => new Guid("1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D");
    }
}
