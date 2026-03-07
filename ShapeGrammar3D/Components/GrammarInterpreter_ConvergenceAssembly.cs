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
    /// Convergence graph preview from SG Assembly. Inputs: Assembly, X spacing (graph width), Y spacing (graph height).
    /// </summary>
    public class GI_ConvergenceAssembly : GH_Component
    {
        internal List<GraphLabel> _labels = new List<GraphLabel>();
        internal double _textHeight = 0.12;
        internal Color _textColor = Color.FromArgb(60, 60, 60);

        public GI_ConvergenceAssembly()
          : base("GI_Convergence (Assembly)", "GI_Conv_A",
              "Convergence graph from Assembly. Plots fitness curves per cluster.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (Hidden || Locked) return;
            foreach (var lbl in _labels)
            {
                Plane pl = new Plane(lbl.Position, lbl.XDir, lbl.YDir);
                args.Display.Draw3dText(lbl.Text, lbl.Color, pl, lbl.Height, "Arial");
            }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly",
                "SG Assembly from GI_Auto6", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen",
                "Which generation(s) to show. Leave empty or -1 = all (default).", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Individual", "Ind",
                "Which individual(s) to include. Leave empty or -1 = all (default).", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Clusters", "Cls",
                "Which cluster(s) to display. Leave empty or -1 = all (default).", GH_ParamAccess.list);
            pManager.AddNumberParameter("X Spacing", "dX",
                "Graph width in model units", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Y Spacing", "dY",
                "Graph height in model units", GH_ParamAccess.item, 6.0);
            pManager.AddPointParameter("Insert Point", "Pt",
                "Base point for graph", GH_ParamAccess.item, Point3d.Origin);
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Ln", "Graph lines", GH_ParamAccess.tree);
            pManager.AddColourParameter("Colours", "Col", "Line colours", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
            pManager.AddCurveParameter("Curves", "Crv", "Convergence curves", GH_ParamAccess.tree);
            pManager.AddColourParameter("CurveColours", "CCol", "Curve colours", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _labels.Clear();

            GH_SGAssembly ghAssembly = null;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly required.");
                return;
            }
            var assembly = ghAssembly.Value;

            List<int> genList = new List<int>();
            List<int> indList = new List<int>();
            List<int> clusterList = new List<int>();
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            DA.GetDataList(3, clusterList);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            if (clusterList.Count == 0) clusterList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            bool allClusters = clusterList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            double graphW = 10.0, graphH = 6.0;
            Point3d insertPt = Point3d.Origin;
            DA.GetData(4, ref graphW);
            DA.GetData(5, ref graphH);
            DA.GetData(6, ref insertPt);
            graphW = Math.Max(1.0, graphW);
            graphH = Math.Max(1.0, graphH);

            var genFilter = allGens ? null : new HashSet<int>(genList.Where(g => g >= 0));
            var fitData = AssemblyToFitData(assembly, genFilter, allInds ? null : indSet);
            var clustData = AssemblyToClustData(assembly, genFilter, allInds ? null : indSet);
            var utilData = AssemblyToUtilData(assembly, genFilter, allInds ? null : indSet);
            var feasData = AssemblyToFeasData(assembly, genFilter, allInds ? null : indSet);
            var rankData = AssemblyToRankData(assembly, genFilter, allInds ? null : indSet);

            bool hasCluster = clustData.Count > 0;
            bool hasRank = rankData.Count > 0;

            int fitIdx = 0;
            var fitSources = new List<(string Name, Dictionary<int, Dictionary<int, double>> Data)>();
            fitSources.Add(("Displacement", fitData));
            if (utilData != null && utilData.Count > 0)
                fitSources.Add(("Avg Util Dev", utilData));
            if (feasData != null && feasData.Count > 0)
                fitSources.Add(("Feasibility", feasData));

            if (fitData.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no fitness data.");
                return;
            }

            var allClusterIds = new SortedSet<int>();
            if (hasCluster)
            {
                foreach (var gen in clustData.Values)
                    foreach (var c in gen.Values)
                        allClusterIds.Add(c);
            }
            if (allClusterIds.Count == 0) allClusterIds.Add(0);
            var selectedClusters = allClusters
                ? allClusterIds.ToList()
                : clusterList.Where(c => c >= 0 && allClusterIds.Contains(c)).Distinct().OrderBy(c => c).ToList();
            if (selectedClusters.Count == 0) selectedClusters = allClusterIds.ToList();

            var sortedGens = fitData.Keys.OrderBy(g => g).ToList();
            int numGens = sortedGens.Count;
            Plane plane = new Plane(insertPt, Vector3d.XAxis, Vector3d.YAxis);

            var linesTree = new GH_Structure<GH_Line>();
            var colorsTree = new GH_Structure<GH_Colour>();
            var curvesTree = new GH_Structure<GH_Curve>();
            var curveColorsTree = new GH_Structure<GH_Colour>();
            double subGraphHeight = fitSources.Count > 1
                ? (graphH - (fitSources.Count - 1) * _textHeight * 3) / fitSources.Count
                : graphH;
            subGraphHeight = Math.Max(1.0, subGraphHeight);
            int totalCurves = 0;

            for (int fi = 0; fi < fitSources.Count; fi++)
            {
                var (sourceName, sourceData) = fitSources[fi];
                var clusterCurves = new Dictionary<int, List<double>>();
                foreach (int cl in selectedClusters)
                    clusterCurves[cl] = new List<double>();
                var avgCurve = new List<double>();
                var topCurve = new List<double>();
                double globalMin = double.MaxValue, globalMax = double.MinValue;

                foreach (int gen in sortedGens)
                {
                    if (!sourceData.ContainsKey(gen))
                    {
                        foreach (int cl in selectedClusters)
                            clusterCurves[cl].Add(double.NaN);
                        avgCurve.Add(double.NaN);
                        topCurve.Add(double.NaN);
                        continue;
                    }
                    var genFit = sourceData[gen];
                    var genClust = hasCluster && clustData.ContainsKey(gen) ? clustData[gen] : null;

                    foreach (int cl in selectedClusters)
                    {
                        double bestFit = double.MaxValue;
                        foreach (var kvp in genFit)
                        {
                            int indCluster = genClust != null && genClust.ContainsKey(kvp.Key) ? genClust[kvp.Key] : 0;
                            if (indCluster != cl) continue;
                            double val = kvp.Value;
                            if (!double.IsInfinity(val) && val < double.MaxValue * 0.5 && val < bestFit)
                                bestFit = val;
                        }
                        clusterCurves[cl].Add(bestFit >= double.MaxValue * 0.5 ? double.NaN : bestFit);
                        if (!double.IsNaN(bestFit)) { if (bestFit < globalMin) globalMin = bestFit; if (bestFit > globalMax) globalMax = bestFit; }
                    }

                    double sum = 0;
                    int count = 0;
                    double topVal = double.MaxValue;
                    foreach (var kvp in genFit)
                    {
                        double val = kvp.Value;
                        if (double.IsInfinity(val) || val >= double.MaxValue * 0.5) continue;
                        sum += val;
                        count++;
                        if (val < topVal) topVal = val;
                    }
                    double avgVal = count > 0 ? sum / count : double.NaN;
                    avgCurve.Add(avgVal);
                    topCurve.Add(topVal < double.MaxValue * 0.5 ? topVal : double.NaN);
                    if (!double.IsNaN(avgVal)) { if (avgVal < globalMin) globalMin = avgVal; if (avgVal > globalMax) globalMax = avgVal; }
                    if (topCurve.Count > 0 && !double.IsNaN(topCurve[topCurve.Count - 1]))
                    {
                        double t = topCurve[topCurve.Count - 1];
                        if (t < globalMin) globalMin = t;
                        if (t > globalMax) globalMax = t;
                    }
                }

                if (globalMin >= globalMax) globalMax = globalMin + 1.0;
                double yRange = globalMax - globalMin;
                double yPadding = yRange * 0.05;
                double yMin = globalMin - yPadding;
                double yMax = globalMax + yPadding;
                double yRangeTotal = yMax - yMin;

                double yOffset = fi * (subGraphHeight + _textHeight * 3);
                Point3d graphOrigin = plane.Origin + plane.YAxis * (graphH - yOffset - subGraphHeight);
                Color mainGridColor = Color.Black;
                Color supportGridColor = Color.FromArgb(180, 180, 180);
                GH_Path axisPath = new GH_Path(fi, 0);
                GH_Path gridPath = new GH_Path(fi, 1);

                linesTree.Append(new GH_Line(new Line(graphOrigin, graphOrigin + plane.XAxis * graphW)), axisPath);
                colorsTree.Append(new GH_Colour(mainGridColor), axisPath);
                linesTree.Append(new GH_Line(new Line(graphOrigin, graphOrigin + plane.YAxis * subGraphHeight)), axisPath);
                colorsTree.Append(new GH_Colour(mainGridColor), axisPath);

                int numYTicks = 5;
                for (int t = 1; t < numYTicks; t++)
                {
                    double frac = (double)t / numYTicks;
                    Point3d tickBase = graphOrigin + plane.YAxis * (frac * subGraphHeight);
                    Point3d gridEnd = tickBase + plane.XAxis * graphW;
                    linesTree.Append(new GH_Line(new Line(tickBase, gridEnd)), gridPath);
                    colorsTree.Append(new GH_Colour(supportGridColor), gridPath);
                }

                foreach (int cl in selectedClusters)
                {
                    Color clColor = GetClusterColour(cl, selectedClusters.Count);
                    var curvePoints = new List<Point3d>();
                    GH_Path curvePath = new GH_Path(fi, 100 + cl);

                    for (int gi = 0; gi < numGens; gi++)
                    {
                        double val = clusterCurves[cl][gi];
                        if (double.IsNaN(val)) continue;
                        double xFrac = numGens > 1 ? (double)gi / (numGens - 1) : 0.5;
                        double yFrac = yRangeTotal > 0 ? (val - yMin) / yRangeTotal : 0.5;
                        yFrac = Math.Clamp(yFrac, 0.0, 1.0);
                        Point3d pt = graphOrigin + plane.XAxis * (xFrac * graphW) + plane.YAxis * (yFrac * subGraphHeight);
                        curvePoints.Add(pt);
                        if (curvePoints.Count >= 2)
                        {
                            var p1 = curvePoints[curvePoints.Count - 2];
                            var p2 = curvePoints[curvePoints.Count - 1];
                            linesTree.Append(new GH_Line(new Line(p1, p2)), curvePath);
                            colorsTree.Append(new GH_Colour(clColor), curvePath);
                        }
                    }
                    if (curvePoints.Count >= 2)
                    {
                        curvesTree.Append(new GH_Curve(new Polyline(curvePoints).ToNurbsCurve()), new GH_Path(fi, totalCurves));
                        curveColorsTree.Append(new GH_Colour(clColor), new GH_Path(fi, totalCurves));
                        totalCurves++;
                    }
                }

                Color avgColor = Color.FromArgb(140, 140, 140);
                Color bestColor = Color.FromArgb(40, 40, 40);
                var avgPoints = new List<Point3d>();
                var topPoints = new List<Point3d>();
                for (int gi = 0; gi < numGens; gi++)
                {
                    if (!double.IsNaN(avgCurve[gi]))
                    {
                        double xFrac = numGens > 1 ? (double)gi / (numGens - 1) : 0.5;
                        double yFrac = yRangeTotal > 0 ? (avgCurve[gi] - yMin) / yRangeTotal : 0.5;
                        yFrac = Math.Clamp(yFrac, 0.0, 1.0);
                        avgPoints.Add(graphOrigin + plane.XAxis * (xFrac * graphW) + plane.YAxis * (yFrac * subGraphHeight));
                    }
                    if (!double.IsNaN(topCurve[gi]))
                    {
                        double xFrac = numGens > 1 ? (double)gi / (numGens - 1) : 0.5;
                        double yFrac = yRangeTotal > 0 ? (topCurve[gi] - yMin) / yRangeTotal : 0.5;
                        yFrac = Math.Clamp(yFrac, 0.0, 1.0);
                        topPoints.Add(graphOrigin + plane.XAxis * (xFrac * graphW) + plane.YAxis * (yFrac * subGraphHeight));
                    }
                }
                if (avgPoints.Count >= 2)
                {
                    curvesTree.Append(new GH_Curve(new Polyline(avgPoints).ToNurbsCurve()), new GH_Path(fi, totalCurves));
                    curveColorsTree.Append(new GH_Colour(avgColor), new GH_Path(fi, totalCurves));
                    totalCurves++;
                }
                if (topPoints.Count >= 2)
                {
                    curvesTree.Append(new GH_Curve(new Polyline(topPoints).ToNurbsCurve()), new GH_Path(fi, totalCurves));
                    curveColorsTree.Append(new GH_Colour(bestColor), new GH_Path(fi, totalCurves));
                    totalCurves++;
                }

                _labels.Add(new GraphLabel
                {
                    Position = graphOrigin + plane.YAxis * (subGraphHeight + _textHeight * 0.5),
                    Text = sourceName,
                    XDir = plane.XAxis,
                    YDir = plane.YAxis,
                    Height = _textHeight * 1.2,
                    Color = Color.FromArgb(40, 40, 40)
                });
            } // end for fi

            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, colorsTree);
            DA.SetData(2, string.Format("Convergence from Assembly: {0} gens, {1} curves", numGens, totalCurves));
            DA.SetDataTree(3, curvesTree);
            DA.SetDataTree(4, curveColorsTree);
        }

        private static Dictionary<int, Dictionary<int, double>> AssemblyToFitData(SGShapeGrammar3DAssembly a, HashSet<int> genFilter, HashSet<int> indFilter)
        {
            var d = new Dictionary<int, Dictionary<int, double>>();
            if (a?.Generations == null) return d;
            foreach (var g in a.Generations)
            {
                if (genFilter != null && !genFilter.Contains(g.Generation)) continue;
                var gen = new Dictionary<int, double>();
                for (int i = 0; i < g.Individuals.Count; i++)
                {
                    if (indFilter != null && !indFilter.Contains(i)) continue;
                    gen[i] = g.Individuals[i].Fitness;
                }
                if (gen.Count > 0) d[g.Generation] = gen;
            }
            return d;
        }
        private static Dictionary<int, Dictionary<int, int>> AssemblyToClustData(SGShapeGrammar3DAssembly a, HashSet<int> genFilter, HashSet<int> indFilter)
        {
            var d = new Dictionary<int, Dictionary<int, int>>();
            if (a?.Generations == null) return d;
            foreach (var g in a.Generations)
            {
                if (genFilter != null && !genFilter.Contains(g.Generation)) continue;
                var gen = new Dictionary<int, int>();
                for (int i = 0; i < g.Individuals.Count; i++)
                {
                    if (indFilter != null && !indFilter.Contains(i)) continue;
                    gen[i] = g.Individuals[i].ClustGrp;
                }
                if (gen.Count > 0) d[g.Generation] = gen;
            }
            return d;
        }
        private static Dictionary<int, Dictionary<int, double>> AssemblyToUtilData(SGShapeGrammar3DAssembly a, HashSet<int> genFilter, HashSet<int> indFilter)
        {
            var d = new Dictionary<int, Dictionary<int, double>>();
            if (a?.Generations == null) return d;
            foreach (var g in a.Generations)
            {
                if (genFilter != null && !genFilter.Contains(g.Generation)) continue;
                var gen = new Dictionary<int, double>();
                for (int i = 0; i < g.Individuals.Count; i++)
                {
                    if (indFilter != null && !indFilter.Contains(i)) continue;
                    gen[i] = g.Individuals[i].ObjUtil;
                }
                if (gen.Count > 0) d[g.Generation] = gen;
            }
            return d;
        }
        private static Dictionary<int, Dictionary<int, double>> AssemblyToFeasData(SGShapeGrammar3DAssembly a, HashSet<int> genFilter, HashSet<int> indFilter)
        {
            var d = new Dictionary<int, Dictionary<int, double>>();
            if (a?.Generations == null) return d;
            foreach (var g in a.Generations)
            {
                if (genFilter != null && !genFilter.Contains(g.Generation)) continue;
                var gen = new Dictionary<int, double>();
                for (int i = 0; i < g.Individuals.Count; i++)
                {
                    if (indFilter != null && !indFilter.Contains(i)) continue;
                    gen[i] = g.Individuals[i].ObjFeas;
                }
                if (gen.Count > 0) d[g.Generation] = gen;
            }
            return d;
        }
        private static Dictionary<int, Dictionary<int, int>> AssemblyToRankData(SGShapeGrammar3DAssembly a, HashSet<int> genFilter, HashSet<int> indFilter)
        {
            var d = new Dictionary<int, Dictionary<int, int>>();
            if (a?.Generations == null) return d;
            foreach (var g in a.Generations)
            {
                if (genFilter != null && !genFilter.Contains(g.Generation)) continue;
                var gen = new Dictionary<int, int>();
                for (int i = 0; i < g.Individuals.Count; i++)
                {
                    if (indFilter != null && !indFilter.Contains(i)) continue;
                    gen[i] = g.Individuals[i].Rank;
                }
                if (gen.Count > 0) d[g.Generation] = gen;
            }
            return d;
        }

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0.0, 1.0);
            int g, b;
            if (t <= 0.5)
            {
                double s = t / 0.5;
                g = Math.Clamp((int)(s * 255), 0, 255);
                b = 255;
            }
            else
            {
                double s = (t - 0.5) / 0.5;
                g = 255;
                b = Math.Clamp((int)((1.0 - s) * 255), 0, 255);
            }
            return Color.FromArgb(0, g, b);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-7890-ABCD-EF0123456781");
    }
}
