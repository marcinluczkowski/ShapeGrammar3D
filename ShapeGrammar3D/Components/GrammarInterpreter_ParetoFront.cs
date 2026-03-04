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
    public class GI_ParetoFront : GH_Component
    {
        internal List<GraphLabel> _labels = new List<GraphLabel>();
        internal double _textHeight = 0.12;
        internal Color _textColor = Color.FromArgb(60, 60, 60);

        public GI_ParetoFront()
          : base("GI_ParetoFront", "GI_Pareto",
              "Visualises the Pareto front of the GA optimisation.\n" +
              "1 objective: displacement vs generation scatter.\n" +
              "2 objectives: displacement vs avg utilisation scatter.\n" +
              "3 objectives: displacement x utilisation x feasibility 3D scatter.",
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
                "Fitness (displacement) tree {generation}(individual) from GI_Auto4",
                GH_ParamAccess.tree);                                                       // 0
            pManager.AddIntegerParameter("ClustGrp", "Clust",
                "Cluster group tree {generation}(individual) from GI_Auto4",
                GH_ParamAccess.tree);                                                       // 1
            pManager.AddPlaneParameter("Plane", "Pln",
                "Plane for the graph (origin = bottom-left corner)",
                GH_ParamAccess.item, Plane.WorldXY);                                        // 2
            pManager.AddIntegerParameter("Num Objectives", "nObj",
                "Number of objectives to display:\n" +
                "1 = displacement vs generation (2D scatter)\n" +
                "2 = displacement vs avg util deviation (2D scatter)\n" +
                "3 = displacement x util x feasibility (3D scatter)",
                GH_ParamAccess.item, 1);                                                    // 3
            pManager.AddIntegerParameter("Generation", "Gen",
                "Which generation(s) to display.\n" +
                "Supply one or many indices. -1 = last generation (default).",
                GH_ParamAccess.list);                                                       // 4
            pManager.AddIntegerParameter("Clusters", "Cls",
                "Which cluster(s) to display. -1 = all (default).",
                GH_ParamAccess.list);                                                       // 5
            pManager.AddNumberParameter("Width", "W",
                "Graph width in model units (X axis extent)", GH_ParamAccess.item, 10.0);   // 6
            pManager.AddNumberParameter("Height", "H",
                "Graph height in model units (Y axis extent)", GH_ParamAccess.item, 6.0);   // 7
            pManager.AddNumberParameter("Depth", "D",
                "Graph depth in model units (Z axis extent, 3-obj mode only)",
                GH_ParamAccess.item, 6.0);                                                  // 8
            pManager.AddNumberParameter("ObjUtil", "ObjU",
                "Avg utilisation deviation tree {generation}(individual) from GI_Auto4.\n" +
                "Required for nObj >= 2.",
                GH_ParamAccess.tree);                                                       // 9
            pManager.AddNumberParameter("ObjFeas", "ObjF",
                "Feasibility objective tree {generation}(individual) from GI_Auto4.\n" +
                "Required for nObj = 3.",
                GH_ParamAccess.tree);                                                       // 10
            pManager.AddIntegerParameter("Pareto Rank", "Rank",
                "Pareto rank tree {generation}(individual) from GI_Auto4.\n" +
                "When connected, rank-0 individuals are drawn larger.",
                GH_ParamAccess.tree);                                                       // 11
            pManager.AddNumberParameter("Text Height", "TxH",
                "Label text height in model units", GH_ParamAccess.item, 0.12);             // 12
            pManager.AddNumberParameter("Point Size", "PtSz",
                "Base point radius in model units", GH_ParamAccess.item, 0.08);             // 13

            pManager[1].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
            pManager[13].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Ln",
                "Axis and grid lines", GH_ParamAccess.tree);                                // 0
            pManager.AddColourParameter("Colours", "Col",
                "Colours matching Lines tree", GH_ParamAccess.tree);                        // 1
            pManager.AddTextParameter("Info", "Info",
                "Summary", GH_ParamAccess.item);                                            // 2
            pManager.AddPointParameter("Points", "Pts",
                "Individual points {generation}(individual)", GH_ParamAccess.tree);         // 3
            pManager.AddColourParameter("PointColours", "PCol",
                "Cluster colour per point matching Pts tree", GH_ParamAccess.tree);         // 4
            pManager.AddNumberParameter("PointSizes", "PSz",
                "Radius per point (larger for rank-0)", GH_ParamAccess.tree);               // 5
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _labels.Clear();

            if (!DA.GetDataTree(0, out GH_Structure<GH_Number> fitnessTree)) return;

            DA.GetDataTree(1, out GH_Structure<GH_Integer> clustTree);
            bool hasCluster = clustTree != null && clustTree.DataCount > 0;

            Plane plane = Plane.WorldXY;
            DA.GetData(2, ref plane);

            int numObj = 1;
            DA.GetData(3, ref numObj);
            numObj = Math.Clamp(numObj, 1, 3);

            var genSelection = new List<int>();
            DA.GetDataList(4, genSelection);
            if (genSelection.Count == 0) genSelection.Add(-1);

            var clusterSelection = new List<int>();
            DA.GetDataList(5, clusterSelection);
            if (clusterSelection.Count == 0) clusterSelection.Add(-1);
            bool allClusters = clusterSelection.Contains(-1);

            double graphW = 10.0, graphH = 6.0, graphD = 6.0;
            DA.GetData(6, ref graphW);
            DA.GetData(7, ref graphH);
            DA.GetData(8, ref graphD);
            graphW = Math.Max(1.0, graphW);
            graphH = Math.Max(1.0, graphH);
            graphD = Math.Max(1.0, graphD);

            DA.GetDataTree(9, out GH_Structure<GH_Number> objUtilTree);
            DA.GetDataTree(10, out GH_Structure<GH_Number> objFeasTree);
            DA.GetDataTree(11, out GH_Structure<GH_Integer> rankTree);

            double textH = 0.12;
            DA.GetData(12, ref textH);
            _textHeight = Math.Max(0.01, textH);

            double ptSize = 0.08;
            DA.GetData(13, ref ptSize);
            ptSize = Math.Max(0.01, ptSize);

            // --- Parse trees ---
            var fitData = ParseNumberTree(fitnessTree);
            var clustData = ParseIntTree(clustTree);
            var utilData = objUtilTree != null ? ParseNumberTree(objUtilTree) : null;
            var feasData = objFeasTree != null ? ParseNumberTree(objFeasTree) : null;
            var rankData = rankTree != null ? ParseIntTree(rankTree) : null;

            if (fitData.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Fitness tree is empty.");
                return;
            }

            if (numObj >= 2 && (utilData == null || utilData.Count == 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "ObjUtil tree required for 2+ objectives but not connected.");
                return;
            }
            if (numObj >= 3 && (feasData == null || feasData.Count == 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "ObjFeas tree required for 3 objectives but not connected.");
                return;
            }

            // --- Resolve generations ---
            var sortedGens = fitData.Keys.OrderBy(g => g).ToList();
            List<int> selectedGens;
            if (genSelection.Contains(-1))
                selectedGens = new List<int> { sortedGens.Last() };
            else
                selectedGens = genSelection.Where(g => fitData.ContainsKey(g)).ToList();

            if (selectedGens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid generations selected.");
                return;
            }

            // --- Determine clusters ---
            var allClusterIds = new SortedSet<int>();
            if (hasCluster)
                foreach (var gen in clustData.Values)
                    foreach (var c in gen.Values)
                        allClusterIds.Add(c);
            if (allClusterIds.Count == 0) allClusterIds.Add(0);
            int totalClusters = allClusterIds.Max() + 1;

            var selectedClusters = allClusters
                ? allClusterIds.ToList()
                : clusterSelection.Where(c => allClusterIds.Contains(c)).ToList();
            var clusterSet = new HashSet<int>(selectedClusters);

            // --- Collect individual records for selected gens/clusters ---
            var records = new List<IndRecord>();
            foreach (int gen in selectedGens)
            {
                if (!fitData.ContainsKey(gen)) continue;
                var genFit = fitData[gen];
                Dictionary<int, int> genClust = hasCluster && clustData.ContainsKey(gen) ? clustData[gen] : null;
                Dictionary<int, double> genUtil = utilData != null && utilData.ContainsKey(gen) ? utilData[gen] : null;
                Dictionary<int, double> genFeas = feasData != null && feasData.ContainsKey(gen) ? feasData[gen] : null;
                Dictionary<int, int> genRank = rankData != null && rankData.ContainsKey(gen) ? rankData[gen] : null;

                foreach (var kvp in genFit)
                {
                    int ind = kvp.Key;
                    double fit = kvp.Value;
                    if (double.IsInfinity(fit) || fit >= double.MaxValue * 0.5) continue;

                    int cl = 0;
                    if (genClust != null && genClust.ContainsKey(ind)) cl = genClust[ind];
                    if (!clusterSet.Contains(cl)) continue;

                    double util = 0;
                    if (genUtil != null && genUtil.ContainsKey(ind)) util = genUtil[ind];

                    double feas = 0;
                    if (genFeas != null && genFeas.ContainsKey(ind)) feas = genFeas[ind];

                    int rank = -1;
                    if (genRank != null && genRank.ContainsKey(ind)) rank = genRank[ind];

                    records.Add(new IndRecord
                    {
                        Gen = gen,
                        Ind = ind,
                        Fitness = fit,
                        Util = util,
                        Feas = feas,
                        Cluster = cl,
                        Rank = rank
                    });
                }
            }

            if (records.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid individuals for selected filters.");
                return;
            }

            // --- Compute axis ranges ---
            double fitMin = records.Min(r => r.Fitness);
            double fitMax = records.Max(r => r.Fitness);
            PadRange(ref fitMin, ref fitMax);

            double utilMin = 0, utilMax = 1;
            if (numObj >= 2)
            {
                utilMin = records.Min(r => r.Util);
                utilMax = records.Max(r => r.Util);
                PadRange(ref utilMin, ref utilMax);
            }

            double feasMin = 0, feasMax = 1;
            if (numObj >= 3)
            {
                feasMin = records.Min(r => r.Feas);
                feasMax = records.Max(r => r.Feas);
                PadRange(ref feasMin, ref feasMax);
            }

            int genMin = selectedGens.Min();
            int genMax = selectedGens.Max();
            if (numObj == 1 && records.Count > 0)
            {
                genMin = records.Min(r => r.Gen);
                genMax = records.Max(r => r.Gen);
            }
            if (genMin == genMax) genMax = genMin + 1;

            // --- Output trees ---
            var linesTree = new GH_Structure<GH_Line>();
            var colorsTree = new GH_Structure<GH_Colour>();
            var pointsTree = new GH_Structure<GH_Point>();
            var ptColorsTree = new GH_Structure<GH_Colour>();
            var ptSizesTree = new GH_Structure<GH_Number>();

            Point3d origin = plane.Origin;
            Vector3d xDir = plane.XAxis;
            Vector3d yDir = plane.YAxis;
            Vector3d zDir = plane.ZAxis;

            string xLabel, yLabel, zLabel = "";

            if (numObj == 1)
            {
                xLabel = "Generation";
                yLabel = "Displacement";
            }
            else
            {
                xLabel = "Displacement";
                yLabel = "Avg Util Dev";
                if (numObj == 3) zLabel = "Feasibility";
            }

            GH_Path axisPath = new GH_Path(0);
            Color axisColor = Color.FromArgb(120, 120, 120);
            Color gridColor = Color.FromArgb(225, 225, 225);
            Color borderColor = Color.FromArgb(200, 200, 200);

            // ===================== DRAW AXES =====================

            // X axis
            linesTree.Append(new GH_Line(new Line(origin, origin + xDir * graphW)), axisPath);
            colorsTree.Append(new GH_Colour(axisColor), axisPath);

            // Y axis
            linesTree.Append(new GH_Line(new Line(origin, origin + yDir * graphH)), axisPath);
            colorsTree.Append(new GH_Colour(axisColor), axisPath);

            // Top and right borders
            Point3d topLeft = origin + yDir * graphH;
            Point3d topRight = topLeft + xDir * graphW;
            Point3d botRight = origin + xDir * graphW;

            linesTree.Append(new GH_Line(new Line(topLeft, topRight)), axisPath);
            colorsTree.Append(new GH_Colour(borderColor), axisPath);
            linesTree.Append(new GH_Line(new Line(botRight, topRight)), axisPath);
            colorsTree.Append(new GH_Colour(borderColor), axisPath);

            if (numObj == 3)
            {
                // Z axis
                linesTree.Append(new GH_Line(new Line(origin, origin + zDir * graphD)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);

                // Z edge lines to close the box
                Point3d topZ = origin + zDir * graphD;
                linesTree.Append(new GH_Line(new Line(topZ, topZ + xDir * graphW)), axisPath);
                colorsTree.Append(new GH_Colour(borderColor), axisPath);
                linesTree.Append(new GH_Line(new Line(topZ, topZ + yDir * graphH)), axisPath);
                colorsTree.Append(new GH_Colour(borderColor), axisPath);

                // Z-axis label
                _labels.Add(new GraphLabel
                {
                    Position = origin + zDir * (graphD * 0.5) - xDir * (_textHeight * 2) - yDir * (_textHeight * 2),
                    Text = zLabel,
                    XDir = zDir,
                    YDir = xDir,
                    Height = _textHeight,
                    Color = _textColor
                });
            }

            // ===================== TICKS & GRID =====================

            int numTicks = 5;

            // X-axis ticks
            for (int t = 0; t <= numTicks; t++)
            {
                double frac = (double)t / numTicks;
                double xVal;
                if (numObj == 1)
                    xVal = genMin + frac * (genMax - genMin);
                else
                    xVal = fitMin + frac * (fitMax - fitMin);

                Point3d tickBase = origin + xDir * (frac * graphW);
                Point3d tickEnd = tickBase - yDir * (_textHeight * 0.3);

                linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);

                if (t > 0 && t < numTicks)
                {
                    linesTree.Append(new GH_Line(new Line(tickBase, tickBase + yDir * graphH)), axisPath);
                    colorsTree.Append(new GH_Colour(gridColor), axisPath);
                }

                _labels.Add(new GraphLabel
                {
                    Position = tickEnd - yDir * (_textHeight * 1.2),
                    Text = numObj == 1 ? ((int)Math.Round(xVal)).ToString() : FormatValue(xVal),
                    XDir = xDir,
                    YDir = yDir,
                    Height = _textHeight * 0.8,
                    Color = _textColor
                });
            }

            // Y-axis ticks
            for (int t = 0; t <= numTicks; t++)
            {
                double frac = (double)t / numTicks;
                double yVal;
                if (numObj == 1)
                    yVal = fitMin + frac * (fitMax - fitMin);
                else
                    yVal = utilMin + frac * (utilMax - utilMin);

                Point3d tickBase = origin + yDir * (frac * graphH);
                Point3d tickEnd = tickBase - xDir * (_textHeight * 0.3);

                linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);

                if (t > 0 && t < numTicks)
                {
                    linesTree.Append(new GH_Line(new Line(tickBase, tickBase + xDir * graphW)), axisPath);
                    colorsTree.Append(new GH_Colour(gridColor), axisPath);
                }

                _labels.Add(new GraphLabel
                {
                    Position = tickEnd - xDir * (_textHeight * 0.5),
                    Text = FormatValue(yVal),
                    XDir = xDir,
                    YDir = yDir,
                    Height = _textHeight * 0.8,
                    Color = _textColor
                });
            }

            // Z-axis ticks (3-obj only)
            if (numObj == 3)
            {
                for (int t = 0; t <= numTicks; t++)
                {
                    double frac = (double)t / numTicks;
                    double zVal = feasMin + frac * (feasMax - feasMin);

                    Point3d tickBase = origin + zDir * (frac * graphD);
                    Point3d tickEnd = tickBase - xDir * (_textHeight * 0.3);

                    linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                    colorsTree.Append(new GH_Colour(axisColor), axisPath);

                    if (t > 0 && t < numTicks)
                    {
                        linesTree.Append(new GH_Line(new Line(tickBase, tickBase + xDir * graphW)), axisPath);
                        colorsTree.Append(new GH_Colour(gridColor), axisPath);
                    }

                    _labels.Add(new GraphLabel
                    {
                        Position = tickEnd - xDir * (_textHeight * 0.5),
                        Text = FormatValue(zVal),
                        XDir = zDir,
                        YDir = xDir,
                        Height = _textHeight * 0.8,
                        Color = _textColor
                    });
                }
            }

            // ===================== AXIS LABELS =====================

            _labels.Add(new GraphLabel
            {
                Position = origin + xDir * (graphW * 0.5) - yDir * (_textHeight * 3.5),
                Text = xLabel,
                XDir = xDir,
                YDir = yDir,
                Height = _textHeight,
                Color = _textColor
            });

            _labels.Add(new GraphLabel
            {
                Position = origin - xDir * (_textHeight * 6) + yDir * (graphH * 0.5),
                Text = yLabel,
                XDir = yDir,
                YDir = -xDir,
                Height = _textHeight,
                Color = _textColor
            });

            // ===================== PLOT POINTS =====================

            bool hasRank = rankData != null && rankData.Count > 0;
            int rank0Count = 0;

            foreach (var rec in records)
            {
                double xFrac, yFrac, zFrac = 0;

                if (numObj == 1)
                {
                    xFrac = (genMax > genMin) ? (double)(rec.Gen - genMin) / (genMax - genMin) : 0.5;
                    yFrac = (fitMax > fitMin) ? (rec.Fitness - fitMin) / (fitMax - fitMin) : 0.5;
                }
                else
                {
                    xFrac = (fitMax > fitMin) ? (rec.Fitness - fitMin) / (fitMax - fitMin) : 0.5;
                    yFrac = (utilMax > utilMin) ? (rec.Util - utilMin) / (utilMax - utilMin) : 0.5;
                    if (numObj == 3)
                        zFrac = (feasMax > feasMin) ? (rec.Feas - feasMin) / (feasMax - feasMin) : 0.5;
                }

                xFrac = Math.Clamp(xFrac, 0.0, 1.0);
                yFrac = Math.Clamp(yFrac, 0.0, 1.0);
                zFrac = Math.Clamp(zFrac, 0.0, 1.0);

                Point3d pt = origin
                    + xDir * (xFrac * graphW)
                    + yDir * (yFrac * graphH);
                if (numObj == 3)
                    pt += zDir * (zFrac * graphD);

                GH_Path ptPath = new GH_Path(rec.Gen);
                Color clColor = GetClusterColour(rec.Cluster, totalClusters);

                bool isRankZero = hasRank && rec.Rank == 0;
                double size = isRankZero ? ptSize * 1.8 : ptSize;
                if (isRankZero) rank0Count++;

                pointsTree.Append(new GH_Point(pt), ptPath);
                ptColorsTree.Append(new GH_Colour(clColor), ptPath);
                ptSizesTree.Append(new GH_Number(size), ptPath);

                // Draw small cross-hair at each point for visibility
                double half = size * 0.5;
                GH_Path crossPath = new GH_Path(1, rec.Gen);
                linesTree.Append(new GH_Line(new Line(pt - xDir * half, pt + xDir * half)), crossPath);
                colorsTree.Append(new GH_Colour(clColor), crossPath);
                linesTree.Append(new GH_Line(new Line(pt - yDir * half, pt + yDir * half)), crossPath);
                colorsTree.Append(new GH_Colour(clColor), crossPath);

                if (numObj == 3)
                {
                    linesTree.Append(new GH_Line(new Line(pt - zDir * half, pt + zDir * half)), crossPath);
                    colorsTree.Append(new GH_Colour(clColor), crossPath);
                }
            }

            // ===================== LEGEND =====================

            foreach (int cl in selectedClusters)
            {
                Color clColor = GetClusterColour(cl, totalClusters);
                double legendY = graphH - selectedClusters.IndexOf(cl) * _textHeight * 1.8;
                Point3d legendPt = origin + xDir * (graphW + _textHeight * 1.5) + yDir * legendY;

                Point3d ls = legendPt - xDir * (_textHeight * 1.2);
                Point3d le = legendPt - xDir * (_textHeight * 0.2);
                linesTree.Append(new GH_Line(new Line(ls, le)), axisPath);
                colorsTree.Append(new GH_Colour(clColor), axisPath);

                _labels.Add(new GraphLabel
                {
                    Position = legendPt,
                    Text = string.Format("C{0}", cl),
                    XDir = xDir,
                    YDir = yDir,
                    Height = _textHeight * 0.9,
                    Color = clColor
                });
            }

            // ===================== TITLE =====================

            string titleText;
            if (numObj == 1)
                titleText = "Displacement vs Generation";
            else if (numObj == 2)
                titleText = "Pareto Front: Displacement vs Avg Util Dev";
            else
                titleText = "Pareto Front: Disp x Util x Feas";

            string genLabel = selectedGens.Count == 1
                ? string.Format("Gen {0}", selectedGens[0])
                : string.Format("Gens [{0}]", string.Join(",", selectedGens));
            titleText += "  (" + genLabel + ")";

            _labels.Add(new GraphLabel
            {
                Position = origin + yDir * (graphH + _textHeight * 0.5),
                Text = titleText,
                XDir = xDir,
                YDir = yDir,
                Height = _textHeight * 1.2,
                Color = Color.FromArgb(40, 40, 40)
            });

            // ===================== OUTPUTS =====================

            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, colorsTree);
            DA.SetDataTree(3, pointsTree);
            DA.SetDataTree(4, ptColorsTree);
            DA.SetDataTree(5, ptSizesTree);

            string clusterStr = allClusters ? "All" : string.Join(", ", selectedClusters);
            string info = string.Format(
                "Pareto Front ({0} objective{1})\n" +
                "Generations: {2}\n" +
                "Clusters: {3}\n" +
                "Total individuals: {4}\n" +
                "Rank-0 (front): {5}\n" +
                "Graph size: {6:F1} x {7:F1}{8}",
                numObj, numObj > 1 ? "s" : "",
                genLabel, clusterStr, records.Count,
                hasRank ? rank0Count.ToString() : "N/A",
                graphW, graphH,
                numObj == 3 ? string.Format(" x {0:F1}", graphD) : "");
            DA.SetData(2, info);
        }

        #region Helpers

        private static void PadRange(ref double min, ref double max)
        {
            if (max <= min) max = min + 1.0;
            double r = max - min;
            double pad = r * 0.05;
            min -= pad;
            max += pad;
        }

        private static Dictionary<int, Dictionary<int, double>> ParseNumberTree(GH_Structure<GH_Number> tree)
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
                    if (branch[i] != null) dict[i] = branch[i].Value;
                result[gen] = dict;
            }
            return result;
        }

        private static Dictionary<int, Dictionary<int, int>> ParseIntTree(GH_Structure<GH_Integer> tree)
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
                    if (branch[i] != null) dict[i] = branch[i].Value;
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

        private struct IndRecord
        {
            public int Gen;
            public int Ind;
            public double Fitness;
            public double Util;
            public double Feas;
            public int Cluster;
            public int Rank;
        }

        #endregion

        protected override Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("D8F0A3B2-4C6E-5D7F-9B1A-2E3F4A5B6C7D");
    }
}
