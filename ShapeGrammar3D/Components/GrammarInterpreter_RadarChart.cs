using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    #region Custom Attributes

    public class RadarChartAttributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds;
        private RectangleF _btnLabels;
        private RectangleF _btnCluster;

        private const float BTN_H = 22f;
        private const float PAD = 4f;
        private const float MIN_W = 180f;

        private GI_RadarChart Comp => (GI_RadarChart)Owner;

        public RadarChartAttributes(GI_RadarChart owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            RectangleF std = Bounds;
            float w = Math.Max(std.Width, MIN_W);
            float xShift = (w - std.Width) * 0.5f;
            float x = std.X - xShift;

            float y = std.Bottom + PAD * 2;
            float cx = x + PAD;
            float cw = w - PAD * 2;

            _btnLabels = new RectangleF(cx, y, cw, BTN_H);
            y += BTN_H + PAD;

            _btnCluster = new RectangleF(cx, y, cw, BTN_H);
            y += BTN_H + PAD;

            _panelBounds = new RectangleF(x, std.Bottom + PAD, w, y - std.Bottom - PAD);
            Bounds = new RectangleF(x, std.Y, w, y - std.Y);
        }

        protected override void Render(GH_Canvas canvas, Graphics g, GH_CanvasChannel channel)
        {
            base.Render(canvas, g, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.HighQuality;

            using (var path = RoundRect(_panelBounds, 5))
            {
                using (var fill = new SolidBrush(Color.FromArgb(220, 245, 245, 245)))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Color.FromArgb(140, 160, 160, 160), 0.8f))
                    g.DrawPath(pen, path);
            }

            DrawToggle(g, _btnLabels, "Show Labels", Comp.ShowLabels);
            DrawToggle(g, _btnCluster, "Show Cluster ID", Comp.ShowCluster);
            g.SmoothingMode = prev;
        }

        private void DrawToggle(Graphics g, RectangleF r, string text, bool on)
        {
            Color bg = on ? Color.FromArgb(230, 76, 175, 80) : Color.FromArgb(210, 200, 200, 200);
            Color border = on ? Color.FromArgb(56, 142, 60) : Color.FromArgb(165, 165, 165);
            Color fg = on ? Color.White : Color.FromArgb(70, 70, 70);

            using (var path = RoundRect(r, 4))
            {
                using (var fill = new SolidBrush(bg)) g.FillPath(fill, path);
                using (var pen = new Pen(border, 0.8f)) g.DrawPath(pen, path);
            }

            float chk = 13f;
            RectangleF box = new RectangleF(r.X + 6, r.Y + (r.Height - chk) / 2f, chk, chk);
            using (var fill = new SolidBrush(on ? Color.White : Color.FromArgb(230, 230, 230)))
                g.FillRectangle(fill, box);
            using (var pen = new Pen(border, 0.8f))
                g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);

            if (on)
            {
                using (var pen = new Pen(Color.FromArgb(46, 125, 50), 2f))
                {
                    g.DrawLine(pen, box.X + 2, box.Y + chk * 0.5f,
                        box.X + chk * 0.35f, box.Bottom - 2);
                    g.DrawLine(pen, box.X + chk * 0.35f, box.Bottom - 2,
                        box.Right - 2, box.Y + 2);
                }
            }

            RectangleF txt = new RectangleF(box.Right + 6, r.Y, r.Width - chk - 18, r.Height);
            using (var brush = new SolidBrush(fg))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(text, GH_FontServer.Standard, brush, txt, sf);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                if (_btnLabels.Contains(e.CanvasLocation))
                {
                    Owner.RecordUndoEvent("Toggle Labels");
                    Comp.ShowLabels = !Comp.ShowLabels;
                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Handled;
                }
                if (_btnCluster.Contains(e.CanvasLocation))
                {
                    Owner.RecordUndoEvent("Toggle Cluster ID");
                    Comp.ShowCluster = !Comp.ShowCluster;
                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Handled;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    #endregion

    #region Display data

    internal struct RadarLabel
    {
        public Point3d Position;
        public string Text;
        public Vector3d XDir;
        public Vector3d YDir;
    }

    #endregion

    #region Component

    public class GI_RadarChart : GH_Component
    {
        public bool ShowLabels { get; set; } = true;
        public bool ShowCluster { get; set; } = true;

        internal List<RadarLabel> _axisLabels = new List<RadarLabel>();
        internal List<RadarLabel> _clusterLabels = new List<RadarLabel>();
        internal double _textHeight = 0.15;
        internal Color _textColor = Color.Black;

        public GI_RadarChart()
            : base("Radar Chart", "Radar",
                  "Visualises n-dimensional metric fingerprints as radar / spider charts " +
                  "arranged in a generation x individual grid.",
                  UT.CAT, UT.GR_DATA_PREVIEW)
        { }

        public override void CreateAttributes()
        {
            m_attributes = new RadarChartAttributes(this);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("ShowLabels", ShowLabels);
            writer.SetBoolean("ShowCluster", ShowCluster);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("ShowLabels")) ShowLabels = reader.GetBoolean("ShowLabels");
            if (reader.ItemExists("ShowCluster")) ShowCluster = reader.GetBoolean("ShowCluster");
            return base.Read(reader);
        }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (Hidden || Locked) return;

            Color textColor = _textColor;

            if (ShowLabels && _axisLabels.Count > 0)
            {
                foreach (var lbl in _axisLabels)
                {
                    Plane pl = new Plane(lbl.Position, lbl.XDir, lbl.YDir);
                    args.Display.Draw3dText(lbl.Text, textColor, pl, _textHeight, "Arial");
                }
            }

            if (ShowCluster && _clusterLabels.Count > 0)
            {
                foreach (var lbl in _clusterLabels)
                {
                    Plane pl = new Plane(lbl.Position, lbl.XDir, lbl.YDir);
                    args.Display.Draw3dText(lbl.Text, textColor, pl, _textHeight, "Arial");
                }
            }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("All Metrics", "AllMet",
                "Metric values tree {generation;individual}(metric) from Auto4",
                GH_ParamAccess.tree);                                                   // 0
            pManager.AddTextParameter("Metric Names", "MetNm",
                "Ordered metric axis labels from Auto4",
                GH_ParamAccess.list);                                                   // 1
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display (-1 = last)", GH_ParamAccess.list);      // 2
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices to display (-1 = all)", GH_ParamAccess.list);       // 3
            pManager.AddNumberParameter("X Spacing", "dX",
                "Horizontal spacing between columns", GH_ParamAccess.item, 30.0);      // 4
            pManager.AddNumberParameter("Y Spacing", "dY",
                "Vertical spacing between rows", GH_ParamAccess.item, 10.0);           // 5
            pManager.AddNumberParameter("Radius", "R",
                "Maximum axis length (model units)", GH_ParamAccess.item, 1.0);        // 6
            pManager.AddIntervalParameter("Metric Domains", "MDom",
                "Expected [min, max] domain per metric axis for normalization.\n" +
                "If not supplied, each axis uses observed min–max over displayed individuals (supports negative metrics).",
                GH_ParamAccess.list);                                                   // 7
            pManager.AddIntegerParameter("Cluster Groups", "Clust",
                "Cluster group per individual {generation}(individual) from Auto4",
                GH_ParamAccess.tree);                                                   // 8
            pManager.AddNumberParameter("Text Height", "TxH",
                "Text height in model units for labels", GH_ParamAccess.item, 0.15);   // 9
            pManager.AddPointParameter("Insert Point", "InsPt",
                "Base point for the grid layout", GH_ParamAccess.item, Point3d.Origin); // 10
            pManager.AddColourParameter("Colour", "Col",
                "Text and label colour", GH_ParamAccess.item, Color.Black);             // 11
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional: orient chart grid (XY through Insert Pt) onto this plane, e.g. XZ. Leave disconnected for world-XY layout.",
                GH_ParamAccess.item);                                                   // 12

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[12].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Axes", "Axes",
                "Axis lines per chart {col;row}(axis)", GH_ParamAccess.tree);           // 0
            pManager.AddCurveParameter("Polygon", "Poly",
                "Data polygon per chart {col;row}", GH_ParamAccess.tree);               // 1
            pManager.AddTextParameter("Info", "Info",
                "Summary", GH_ParamAccess.item);                                        // 2
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _axisLabels.Clear();
            _clusterLabels.Clear();

            if (!DA.GetDataTree(0, out GH_Structure<GH_Number> metricsTree)) return;

            var metricNames = new List<string>();
            if (!DA.GetDataList(1, metricNames) || metricNames.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No metric names provided.");
                return;
            }

            var genList = new List<int>();
            var indList = new List<int>();
            DA.GetDataList(2, genList);
            DA.GetDataList(3, indList);

            double xSpacing = 30.0, ySpacing = 10.0, radius = 1.0;
            DA.GetData(4, ref xSpacing);
            DA.GetData(5, ref ySpacing);
            DA.GetData(6, ref radius);
            if (radius <= 0) radius = 1.0;

            Point3d insertPt = Point3d.Origin;
            DA.GetData(10, ref insertPt);

            var rawDomains = new List<GH_Interval>();
            DA.GetDataList(7, rawDomains);
            List<Interval> domains = null;
            if (rawDomains.Count > 0)
                domains = rawDomains.Select(d => d.Value).ToList();

            DA.GetDataTree(8, out GH_Structure<GH_Integer> clustTree);

            double textH = 0.15;
            DA.GetData(9, ref textH);
            _textHeight = Math.Max(0.01, textH);

            Color inputColour = Color.Black;
            DA.GetData(11, ref inputColour);
            _textColor = inputColour;

            Transform dispXf = PreviewLayoutTransforms.GetOptionalDisplayTransform(DA, 12, insertPt);

            int numAxes = metricNames.Count;
            if (numAxes < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Need at least 2 metrics for a radar chart.");
                return;
            }

            bool hasDomains = domains != null && domains.Count >= numAxes;

            // --- Parse metrics tree ---
            var allPaths = metricsTree.Paths;
            if (allPaths.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Metrics tree is empty.");
                return;
            }

            var dataByGenInd = new SortedDictionary<int, SortedDictionary<int, double[]>>();
            foreach (GH_Path p in allPaths)
            {
                if (p.Length < 2) continue;
                int gen = p.Indices[0];
                int ind = p.Indices[1];

                var branch = metricsTree.get_Branch(p);
                double[] vals = new double[numAxes];
                for (int m = 0; m < Math.Min(numAxes, branch.Count); m++)
                {
                    if (branch[m] is GH_Number ghNum)
                        vals[m] = ghNum.Value;
                }

                if (!dataByGenInd.ContainsKey(gen))
                    dataByGenInd[gen] = new SortedDictionary<int, double[]>();
                dataByGenInd[gen][ind] = vals;
            }

            if (dataByGenInd.Count == 0) return;

            // --- Parse cluster tree ---
            var clusterByGenInd = new Dictionary<int, Dictionary<int, int>>();
            if (clustTree != null)
            {
                foreach (GH_Path cp in clustTree.Paths)
                {
                    if (cp.Length < 1) continue;
                    int cGen = cp.Indices[0];
                    var cBranch = clustTree.get_Branch(cp);
                    if (!clusterByGenInd.ContainsKey(cGen))
                        clusterByGenInd[cGen] = new Dictionary<int, int>();
                    for (int ci = 0; ci < cBranch.Count; ci++)
                    {
                        if (cBranch[ci] is GH_Integer ghInt)
                            clusterByGenInd[cGen][ci] = ghInt.Value;
                    }
                }
            }

            // --- Filter generations ---
            bool allGens = genList.Count == 0 || (genList.Count == 1 && genList[0] == -1);
            List<int> selectedGens;
            if (allGens)
            {
                int lastGen = dataByGenInd.Keys.Max();
                selectedGens = new List<int> { lastGen };
            }
            else
            {
                selectedGens = genList.Where(g => dataByGenInd.ContainsKey(g)).ToList();
            }
            if (selectedGens.Count == 0) return;

            bool allInds = indList.Count == 0 || (indList.Count == 1 && indList[0] == -1);
            var indSet = new HashSet<int>(indList);

            // --- Per-axis normalization ---
            double[] axisMin = new double[numAxes];
            double[] axisRange = new double[numAxes];

            if (hasDomains)
            {
                for (int m = 0; m < numAxes; m++)
                {
                    axisMin[m] = domains[m].Min;
                    axisRange[m] = domains[m].Max - domains[m].Min;
                    if (axisRange[m] <= 0) axisRange[m] = 1.0;
                }
            }
            else
            {
                var axisLo = new double[numAxes];
                var axisHi = new double[numAxes];
                for (int m = 0; m < numAxes; m++)
                {
                    axisLo[m] = double.PositiveInfinity;
                    axisHi[m] = double.NegativeInfinity;
                }

                foreach (int gen in selectedGens)
                {
                    foreach (var kvp in dataByGenInd[gen])
                    {
                        if (!allInds && !indSet.Contains(kvp.Key)) continue;
                        for (int m = 0; m < numAxes; m++)
                        {
                            double v = kvp.Value[m];
                            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                            if (v < axisLo[m]) axisLo[m] = v;
                            if (v > axisHi[m]) axisHi[m] = v;
                        }
                    }
                }

                const double epsSpan = 1e-15;
                for (int m = 0; m < numAxes; m++)
                {
                    if (double.IsInfinity(axisLo[m]) || double.IsInfinity(axisHi[m]))
                    {
                        axisMin[m] = 0;
                        axisRange[m] = 1.0;
                        continue;
                    }

                    axisMin[m] = axisLo[m];
                    double span = axisHi[m] - axisLo[m];
                    if (span <= epsSpan)
                    {
                        axisMin[m] = axisHi[m] - 0.5;
                        axisRange[m] = 1.0;
                    }
                    else
                        axisRange[m] = span;
                }
            }

            // --- Axis directions ---
            double[] axisAngles = new double[numAxes];
            Vector3d[] axisDirs = new Vector3d[numAxes];
            for (int m = 0; m < numAxes; m++)
            {
                double angle = 2.0 * Math.PI * m / numAxes - Math.PI / 2.0;
                axisAngles[m] = angle;
                axisDirs[m] = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
            }

            int maxNameLen = metricNames.Max(n => n.Length);
            int maxValLen = 15;
            int maxChars = Math.Max(maxNameLen, maxValLen);
            double labelTextWidth = _textHeight * maxChars * 0.55;
            double labelGap = _textHeight * 0.5;

            // --- Build geometry output ---
            var axesTree = new GH_Structure<GH_Line>();
            var polyTree = new GH_Structure<GH_Curve>();

            int col = 0;
            int totalCharts = 0;

            foreach (int gen in selectedGens)
            {
                var genData = dataByGenInd[gen];
                int row = 0;

                foreach (var kvp in genData)
                {
                    int indIdx = kvp.Key;
                    if (!allInds && !indSet.Contains(indIdx)) continue;

                    double[] vals = kvp.Value;
                    Point3d c = new Point3d(
                        insertPt.X + col * xSpacing,
                        insertPt.Y - row * ySpacing,
                        insertPt.Z);
                    GH_Path outPath = new GH_Path(col, row);

                    var polygonPts = new List<Point3d>();

                    for (int m = 0; m < numAxes; m++)
                    {
                        Line axisLn = new Line(c, c + axisDirs[m] * radius);
                        axisLn.Transform(dispXf);
                        axesTree.Append(new GH_Line(axisLn), outPath);

                        double norm = axisRange[m] > 0 && !double.IsNaN(vals[m]) && !double.IsInfinity(vals[m])
                            ? (vals[m] - axisMin[m]) / axisRange[m]
                            : 0;
                        double clampedNorm = double.IsNaN(vals[m]) || double.IsInfinity(vals[m])
                            ? 0
                            : Math.Max(0, Math.Min(1, norm));
                        polygonPts.Add(c + axisDirs[m] * (radius * clampedNorm));

                        Vector3d xdir = axisDirs[m];
                        Vector3d ydir = new Vector3d(-axisDirs[m].Y, axisDirs[m].X, 0);
                        bool flipped = xdir.X < -1e-10
                            || (Math.Abs(xdir.X) < 1e-10 && xdir.Y < 0);
                        if (flipped)
                        {
                            xdir = -xdir;
                            ydir = -ydir;
                        }

                        Point3d labelPt;
                        if (flipped)
                            labelPt = c + axisDirs[m] * (radius + labelGap + labelTextWidth);
                        else
                            labelPt = c + axisDirs[m] * (radius + labelGap);

                        Plane namePl = new Plane(labelPt, xdir, ydir);
                        namePl.Transform(dispXf);
                        _axisLabels.Add(new RadarLabel
                        {
                            Position = namePl.Origin,
                            Text = metricNames[m],
                            XDir = namePl.XAxis,
                            YDir = namePl.YAxis
                        });

                        Plane valPl = new Plane(labelPt - ydir * _textHeight * 1.3, xdir, ydir);
                        valPl.Transform(dispXf);
                        _axisLabels.Add(new RadarLabel
                        {
                            Position = valPl.Origin,
                            Text = string.Format("{0:F3} ({1:F2})", vals[m], clampedNorm),
                            XDir = valPl.XAxis,
                            YDir = valPl.YAxis
                        });
                    }

                    if (polygonPts.Count > 0)
                    {
                        for (int pi = 0; pi < polygonPts.Count; pi++)
                            polygonPts[pi].Transform(dispXf);
                        polygonPts.Add(polygonPts[0]);
                        Polyline pl = new Polyline(polygonPts);
                        polyTree.Append(new GH_Curve(pl.ToNurbsCurve()), outPath);
                    }

                    int clustId = -1;
                    if (clusterByGenInd.TryGetValue(gen, out var genClust)
                        && genClust.TryGetValue(indIdx, out int cid))
                        clustId = cid;

                    Plane clPl = new Plane(c + new Vector3d(0, -radius * 1.25, 0), Vector3d.XAxis, Vector3d.YAxis);
                    clPl.Transform(dispXf);
                    _clusterLabels.Add(new RadarLabel
                    {
                        Position = clPl.Origin,
                        Text = string.Format("C{0} [G{1} I{2}]", clustId, gen, indIdx),
                        XDir = clPl.XAxis,
                        YDir = clPl.YAxis
                    });

                    row++;
                    totalCharts++;
                }
                col++;
            }

            DA.SetDataTree(0, axesTree);
            DA.SetDataTree(1, polyTree);

            string normMode = hasDomains ? "user-defined domains" : "observed min–max";
            string info = string.Format(
                "Radar Charts: {0}\nAxes: {1}\nGenerations shown: {2}\n" +
                "Radius: {3}\nText Height: {4}\nNormalization: {5}\nLabels: {6}\nClusters: {7}",
                totalCharts, numAxes,
                string.Join(", ", selectedGens),
                radius, _textHeight, normMode,
                ShowLabels ? "ON" : "OFF",
                ShowCluster ? "ON" : "OFF");
            DA.SetData(2, info);
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("F6A7B8C9-0D1E-2F3A-4B5C-6D7E8F9A0B12");
    }

    #endregion
}
