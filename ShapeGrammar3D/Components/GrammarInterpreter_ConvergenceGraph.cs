using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GI_ConvergenceGraph : GH_Component
    {
        internal List<GraphLabel> _labels = new List<GraphLabel>();
        internal double _textHeight = 0.12;
        internal Color _textColor = Color.FromArgb(60, 60, 60);

        public GI_ConvergenceGraph()
          : base("GI_Convergence", "GI_Conv",
              "Plots fitness convergence curves per cluster on a 2D graph placed in a given plane",
              UT.CAT, UT.GR_INT)
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
            pManager.AddNumberParameter("Fitness", "Fit",
                "Fitness tree {generation}(individual) from GI_Auto4 output",
                GH_ParamAccess.tree);                                                       // 0
            pManager.AddIntegerParameter("ClustGrp", "Clust",
                "Cluster group tree {generation}(individual) from GI_Auto4",
                GH_ParamAccess.tree);                                                       // 1
            pManager.AddPlaneParameter("Plane", "Pln",
                "Plane on which to draw the 2D graph (origin = bottom-left corner)",
                GH_ParamAccess.item, Plane.WorldXY);                                        // 2
            pManager.AddIntegerParameter("Fitness Index", "FitIdx",
                "Which fitness to plot:\n" +
                "0 = displacement (default, uses Fitness tree)\n" +
                "1 = avg utilization deviation (uses ObjUtil tree)\n" +
                "2 = feasibility (uses ObjFeas tree)\n" +
                "-1 = all three on separate sub-graphs",
                GH_ParamAccess.item, 0);                                                    // 3
            pManager.AddIntegerParameter("Clusters", "Cls",
                "Which cluster(s) to display. Supply one or many indices.\n" +
                "-1 = all clusters (default).",
                GH_ParamAccess.list);                                                       // 4
            pManager.AddNumberParameter("Width", "W",
                "Graph width in model units", GH_ParamAccess.item, 10.0);                   // 5
            pManager.AddNumberParameter("Height", "H",
                "Graph height in model units", GH_ParamAccess.item, 6.0);                   // 6
            pManager.AddNumberParameter("ObjUtil", "ObjU",
                "Avg utilization deviation tree {generation}(individual) from GI_Auto4. " +
                "Only needed when FitIdx = 1 or -1.",
                GH_ParamAccess.tree);                                                       // 7
            pManager.AddNumberParameter("ObjFeas", "ObjF",
                "Feasibility objective tree {generation}(individual) from GI_Auto4. " +
                "Only needed when FitIdx = 2 or -1.",
                GH_ParamAccess.tree);                                                       // 8
            pManager.AddNumberParameter("Text Height", "TxH",
                "Label text height in model units", GH_ParamAccess.item, 0.12);             // 9
            pManager.AddNumberParameter("Line Weight", "LnW",
                "Curve thickness in pixels", GH_ParamAccess.item, 2.0);                     // 10

            pManager[1].Optional = true;
            pManager[4].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Ln",
                "All graph lines (axes, grid, curves)", GH_ParamAccess.tree);               // 0
            pManager.AddColourParameter("Colours", "Col",
                "Colours matching Lines tree", GH_ParamAccess.tree);                        // 1
            pManager.AddTextParameter("Info", "Info",
                "Summary", GH_ParamAccess.item);                                            // 2
            pManager.AddCurveParameter("Curves", "Crv",
                "Convergence polylines per cluster {fitness_index}(cluster)",
                GH_ParamAccess.tree);                                                       // 3
            pManager.AddColourParameter("CurveColours", "CCol",
                "Colours per curve matching Curves tree", GH_ParamAccess.tree);             // 4
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _labels.Clear();

            if (!DA.GetDataTree(0, out GH_Structure<GH_Number> fitnessTree)) return;

            DA.GetDataTree(1, out GH_Structure<GH_Integer> clustTree);
            bool hasCluster = clustTree != null && clustTree.DataCount > 0;

            Plane plane = Plane.WorldXY;
            DA.GetData(2, ref plane);

            int fitIdx = 0;
            DA.GetData(3, ref fitIdx);
            fitIdx = Math.Clamp(fitIdx, -1, 2);

            var clusterSelection = new List<int>();
            DA.GetDataList(4, clusterSelection);
            if (clusterSelection.Count == 0) clusterSelection.Add(-1);
            bool allClusters = clusterSelection.Contains(-1);

            double graphW = 10.0, graphH = 6.0;
            DA.GetData(5, ref graphW);
            DA.GetData(6, ref graphH);
            graphW = Math.Max(1.0, graphW);
            graphH = Math.Max(1.0, graphH);

            DA.GetDataTree(7, out GH_Structure<GH_Number> objUtilTree);
            DA.GetDataTree(8, out GH_Structure<GH_Number> objFeasTree);

            double textH = 0.12;
            DA.GetData(9, ref textH);
            _textHeight = Math.Max(0.01, textH);

            double lineWeight = 2.0;
            DA.GetData(10, ref lineWeight);

            // --- Parse data trees into dictionaries: gen -> ind -> value ---
            var fitData = ParseTree(fitnessTree);
            var clustData = ParseClusterTree(clustTree);
            var utilData = objUtilTree != null ? ParseTree(objUtilTree) : null;
            var feasData = objFeasTree != null ? ParseTree(objFeasTree) : null;

            if (fitData.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Fitness tree is empty.");
                return;
            }

            // --- Determine which fitness sources to plot ---
            var fitSources = new List<(string Name, Dictionary<int, Dictionary<int, double>> Data)>();
            if (fitIdx == 0 || fitIdx == -1)
                fitSources.Add(("Displacement", fitData));
            if ((fitIdx == 1 || fitIdx == -1) && utilData != null && utilData.Count > 0)
                fitSources.Add(("Avg Util Dev", utilData));
            if ((fitIdx == 2 || fitIdx == -1) && feasData != null && feasData.Count > 0)
                fitSources.Add(("Feasibility", feasData));

            if (fitSources.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No data available for the selected fitness index.");
                return;
            }

            // --- Determine all clusters present ---
            var allClusterIds = new SortedSet<int>();
            if (hasCluster)
            {
                foreach (var gen in clustData.Values)
                    foreach (var c in gen.Values)
                        allClusterIds.Add(c);
            }
            if (allClusterIds.Count == 0)
                allClusterIds.Add(0);

            int totalClusters = allClusterIds.Max() + 1;

            var selectedClusters = allClusters
                ? allClusterIds.ToList()
                : clusterSelection.Where(c => allClusterIds.Contains(c)).ToList();

            if (selectedClusters.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "None of the selected clusters exist in the data.");
                return;
            }

            // --- Build convergence data: per fitness source, per cluster, best fitness per gen ---
            var sortedGens = fitData.Keys.OrderBy(g => g).ToList();
            int numGens = sortedGens.Count;

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

                double yOffset = fi * (subGraphHeight + _textHeight * 3);
                Point3d graphOrigin = plane.Origin
                    + plane.XAxis * 0
                    + plane.YAxis * (graphH - yOffset - subGraphHeight);

                // --- Compute best fitness per generation per cluster ---
                var clusterCurves = new Dictionary<int, List<double>>();
                foreach (int cl in selectedClusters)
                    clusterCurves[cl] = new List<double>();

                double globalMin = double.MaxValue;
                double globalMax = double.MinValue;

                foreach (int gen in sortedGens)
                {
                    if (!sourceData.ContainsKey(gen))
                    {
                        foreach (int cl in selectedClusters)
                            clusterCurves[cl].Add(double.NaN);
                        continue;
                    }

                    var genFit = sourceData[gen];
                    Dictionary<int, int> genClust = null;
                    if (hasCluster && clustData.ContainsKey(gen))
                        genClust = clustData[gen];

                    foreach (int cl in selectedClusters)
                    {
                        double bestFit = double.MaxValue;
                        foreach (var kvp in genFit)
                        {
                            int indCluster = 0;
                            if (genClust != null && genClust.ContainsKey(kvp.Key))
                                indCluster = genClust[kvp.Key];

                            if (indCluster != cl) continue;

                            double val = kvp.Value;
                            if (double.IsInfinity(val) || val >= double.MaxValue * 0.5)
                                continue;
                            if (val < bestFit) bestFit = val;
                        }

                        if (bestFit >= double.MaxValue * 0.5)
                            bestFit = double.NaN;

                        clusterCurves[cl].Add(bestFit);

                        if (!double.IsNaN(bestFit))
                        {
                            if (bestFit < globalMin) globalMin = bestFit;
                            if (bestFit > globalMax) globalMax = bestFit;
                        }
                    }
                }

                if (globalMin >= globalMax)
                {
                    globalMax = globalMin + 1.0;
                }

                double yRange = globalMax - globalMin;
                double yPadding = yRange * 0.05;
                double yMin = globalMin - yPadding;
                double yMax = globalMax + yPadding;
                double yRangeTotal = yMax - yMin;

                GH_Path axisPath = new GH_Path(fi, 0);
                Color axisColor = Color.FromArgb(120, 120, 120);

                // X axis (bottom)
                Point3d xStart = graphOrigin;
                Point3d xEnd = graphOrigin + plane.XAxis * graphW;
                linesTree.Append(new GH_Line(new Line(xStart, xEnd)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);

                // Y axis (left)
                Point3d yStart = graphOrigin;
                Point3d yEnd = graphOrigin + plane.YAxis * subGraphHeight;
                linesTree.Append(new GH_Line(new Line(yStart, yEnd)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);

                // Top border
                Point3d topLeft = graphOrigin + plane.YAxis * subGraphHeight;
                Point3d topRight = topLeft + plane.XAxis * graphW;
                linesTree.Append(new GH_Line(new Line(topLeft, topRight)), axisPath);
                colorsTree.Append(new GH_Colour(Color.FromArgb(200, 200, 200)), axisPath);

                // Right border
                Point3d botRight = graphOrigin + plane.XAxis * graphW;
                linesTree.Append(new GH_Line(new Line(botRight, topRight)), axisPath);
                colorsTree.Append(new GH_Colour(Color.FromArgb(200, 200, 200)), axisPath);

                // --- Y-axis tick marks and grid ---
                int numYTicks = 5;
                for (int t = 0; t <= numYTicks; t++)
                {
                    double frac = (double)t / numYTicks;
                    double yVal = yMin + frac * yRangeTotal;
                    Point3d tickBase = graphOrigin + plane.YAxis * (frac * subGraphHeight);
                    Point3d tickEnd = tickBase - plane.XAxis * (_textHeight * 0.3);

                    linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                    colorsTree.Append(new GH_Colour(axisColor), axisPath);

                    // Horizontal grid line
                    if (t > 0 && t < numYTicks)
                    {
                        Point3d gridEnd = tickBase + plane.XAxis * graphW;
                        linesTree.Append(new GH_Line(new Line(tickBase, gridEnd)), axisPath);
                        colorsTree.Append(new GH_Colour(Color.FromArgb(230, 230, 230)), axisPath);
                    }

                    _labels.Add(new GraphLabel
                    {
                        Position = tickEnd - plane.XAxis * (_textHeight * 0.5),
                        Text = FormatValue(yVal),
                        XDir = plane.XAxis,
                        YDir = plane.YAxis,
                        Height = _textHeight * 0.8,
                        Color = _textColor
                    });
                }

                // --- X-axis tick marks ---
                int tickInterval = Math.Max(1, numGens / 10);
                for (int gi = 0; gi < numGens; gi++)
                {
                    if (gi % tickInterval != 0 && gi != numGens - 1) continue;

                    double xFrac = numGens > 1 ? (double)gi / (numGens - 1) : 0.5;
                    Point3d tickBase = graphOrigin + plane.XAxis * (xFrac * graphW);
                    Point3d tickEnd = tickBase - plane.YAxis * (_textHeight * 0.3);

                    linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                    colorsTree.Append(new GH_Colour(axisColor), axisPath);

                    // Vertical grid line
                    if (gi > 0 && gi < numGens - 1)
                    {
                        Point3d gridTop = tickBase + plane.YAxis * subGraphHeight;
                        linesTree.Append(new GH_Line(new Line(tickBase, gridTop)), axisPath);
                        colorsTree.Append(new GH_Colour(Color.FromArgb(235, 235, 235)), axisPath);
                    }

                    _labels.Add(new GraphLabel
                    {
                        Position = tickEnd - plane.YAxis * (_textHeight * 1.2),
                        Text = sortedGens[gi].ToString(),
                        XDir = plane.XAxis,
                        YDir = plane.YAxis,
                        Height = _textHeight * 0.8,
                        Color = _textColor
                    });
                }

                // --- Axis labels ---
                _labels.Add(new GraphLabel
                {
                    Position = graphOrigin + plane.XAxis * (graphW * 0.5) - plane.YAxis * (_textHeight * 3.5),
                    Text = "Generation",
                    XDir = plane.XAxis,
                    YDir = plane.YAxis,
                    Height = _textHeight,
                    Color = _textColor
                });

                _labels.Add(new GraphLabel
                {
                    Position = graphOrigin - plane.XAxis * (_textHeight * 6) + plane.YAxis * (subGraphHeight * 0.5),
                    Text = sourceName,
                    XDir = plane.YAxis,
                    YDir = -plane.XAxis,
                    Height = _textHeight,
                    Color = _textColor
                });

                // --- Plot convergence curves ---
                foreach (int cl in selectedClusters)
                {
                    Color clColor = GetClusterColour(cl, totalClusters);
                    var curvePoints = new List<Point3d>();
                    GH_Path curvePath = new GH_Path(fi, cl);

                    for (int gi = 0; gi < numGens; gi++)
                    {
                        double val = clusterCurves[cl][gi];
                        if (double.IsNaN(val)) continue;

                        double xFrac = numGens > 1 ? (double)gi / (numGens - 1) : 0.5;
                        double yFrac = yRangeTotal > 0 ? (val - yMin) / yRangeTotal : 0.5;
                        yFrac = Math.Clamp(yFrac, 0.0, 1.0);

                        Point3d pt = graphOrigin
                            + plane.XAxis * (xFrac * graphW)
                            + plane.YAxis * (yFrac * subGraphHeight);
                        curvePoints.Add(pt);

                        // Segment line from previous point
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
                        var polyline = new Polyline(curvePoints);
                        curvesTree.Append(new GH_Curve(polyline.ToNurbsCurve()), new GH_Path(fi));
                        curveColorsTree.Append(new GH_Colour(clColor), new GH_Path(fi));
                        totalCurves++;
                    }

                    // Legend entry
                    double legendX = graphW + _textHeight * 1.5;
                    double legendY = subGraphHeight - (selectedClusters.IndexOf(cl)) * _textHeight * 1.8;
                    Point3d legendPt = graphOrigin
                        + plane.XAxis * legendX
                        + plane.YAxis * legendY;

                    Point3d legendLineStart = legendPt - plane.XAxis * (_textHeight * 1.2);
                    Point3d legendLineEnd = legendPt - plane.XAxis * (_textHeight * 0.2);
                    linesTree.Append(new GH_Line(new Line(legendLineStart, legendLineEnd)), axisPath);
                    colorsTree.Append(new GH_Colour(clColor), axisPath);

                    _labels.Add(new GraphLabel
                    {
                        Position = legendPt,
                        Text = string.Format("C{0}", cl),
                        XDir = plane.XAxis,
                        YDir = plane.YAxis,
                        Height = _textHeight * 0.9,
                        Color = clColor
                    });
                }

                // Title
                _labels.Add(new GraphLabel
                {
                    Position = graphOrigin + plane.YAxis * (subGraphHeight + _textHeight * 0.5),
                    Text = sourceName,
                    XDir = plane.XAxis,
                    YDir = plane.YAxis,
                    Height = _textHeight * 1.2,
                    Color = Color.FromArgb(40, 40, 40)
                });
            }

            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, colorsTree);
            DA.SetDataTree(3, curvesTree);
            DA.SetDataTree(4, curveColorsTree);

            string clusterStr = allClusters
                ? "All"
                : string.Join(", ", selectedClusters);
            string fitNames = string.Join(", ", fitSources.Select(f => f.Name));
            string info = string.Format(
                "Convergence Graph\n" +
                "Fitness: {0}\n" +
                "Generations: {1}\n" +
                "Clusters shown: {2}\n" +
                "Total curves: {3}\n" +
                "Graph size: {4:F1} x {5:F1}",
                fitNames, numGens, clusterStr, totalCurves, graphW, graphH);
            DA.SetData(2, info);
        }

        private static Dictionary<int, Dictionary<int, double>> ParseTree(GH_Structure<GH_Number> tree)
        {
            var result = new Dictionary<int, Dictionary<int, double>>();
            if (tree == null) return result;

            for (int b = 0; b < tree.PathCount; b++)
            {
                GH_Path path = tree.Paths[b];
                if (path.Length < 1) continue;
                int gen = path[0];

                var branch = tree.Branches[b];
                var dict = new Dictionary<int, double>();
                for (int i = 0; i < branch.Count; i++)
                {
                    if (branch[i] != null)
                        dict[i] = branch[i].Value;
                }
                result[gen] = dict;
            }
            return result;
        }

        private static Dictionary<int, Dictionary<int, int>> ParseClusterTree(GH_Structure<GH_Integer> tree)
        {
            var result = new Dictionary<int, Dictionary<int, int>>();
            if (tree == null) return result;

            for (int b = 0; b < tree.PathCount; b++)
            {
                GH_Path path = tree.Paths[b];
                if (path.Length < 1) continue;
                int gen = path[0];

                var branch = tree.Branches[b];
                var dict = new Dictionary<int, int>();
                for (int i = 0; i < branch.Count; i++)
                {
                    if (branch[i] != null)
                        dict[i] = branch[i].Value;
                }
                result[gen] = dict;
            }
            return result;
        }

        private static string FormatValue(double v)
        {
            double abs = Math.Abs(v);
            if (abs >= 1000) return v.ToString("F0");
            if (abs >= 1) return v.ToString("F2");
            if (abs >= 0.01) return v.ToString("F4");
            return v.ToString("E2");
        }

        /// <summary>
        /// Blue (0,0,255) -> Cyan (0,255,255) -> Green (0,255,0).
        /// Same gradient as GI_Preview.
        /// </summary>
        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1)
                return Color.FromArgb(0, 150, 255);

            double t = (double)cluster / (totalClusters - 1);
            t = Math.Clamp(t, 0.0, 1.0);

            int r = 0;
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

            return Color.FromArgb(r, g, b);
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("C7E9F2A1-3B5D-4E6F-8A0C-1D2E3F4A5B6C");
    }

    internal struct GraphLabel
    {
        public Point3d Position;
        public string Text;
        public Vector3d XDir;
        public Vector3d YDir;
        public double Height;
        public Color Color;
    }
}
