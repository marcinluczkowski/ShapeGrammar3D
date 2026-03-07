using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
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
    #region RadarAssembly Attributes

    public class RadarAssemblyAttributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds, _btnLabels, _btnCluster;
        private const float BTN_H = 22f, PAD = 4f, MIN_W = 180f;
        private GI_RadarAssembly Comp => (GI_RadarAssembly)Owner;

        public RadarAssemblyAttributes(GI_RadarAssembly owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            float w = Math.Max(Bounds.Width, MIN_W);
            float x = Bounds.X - (w - Bounds.Width) * 0.5f;
            float y = Bounds.Bottom + PAD * 2;
            _btnLabels = new RectangleF(x + PAD, y, w - PAD * 2, BTN_H);
            y += BTN_H + PAD;
            _btnCluster = new RectangleF(x + PAD, y, w - PAD * 2, BTN_H);
            _panelBounds = new RectangleF(x, Bounds.Bottom + PAD, w, y - Bounds.Bottom - PAD);
            Bounds = new RectangleF(x, Bounds.Y, w, y - Bounds.Y);
        }

        protected override void Render(GH_Canvas canvas, Graphics g, GH_CanvasChannel channel)
        {
            base.Render(canvas, g, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            using (var path = RoundRect(_panelBounds, 5))
            {
                g.FillPath(new SolidBrush(Color.FromArgb(220, 245, 245, 245)), path);
                g.DrawPath(new Pen(Color.FromArgb(140, 160, 160, 160), 0.8f), path);
            }
            DrawToggle(g, _btnLabels, "Show Labels", Comp.ShowLabels);
            DrawToggle(g, _btnCluster, "Show Cluster ID", Comp.ShowCluster);
        }

        private void DrawToggle(Graphics g, RectangleF r, string text, bool on)
        {
            Color bg = on ? Color.FromArgb(230, 76, 175, 80) : Color.FromArgb(210, 200, 200, 200);
            using (var path = RoundRect(r, 4))
            {
                g.FillPath(new SolidBrush(bg), path);
                g.DrawPath(new Pen(on ? Color.FromArgb(56, 142, 60) : Color.FromArgb(165, 165, 165), 0.8f), path);
            }
            float chk = 13f;
            var box = new RectangleF(r.X + 6, r.Y + (r.Height - chk) / 2f, chk, chk);
            g.FillRectangle(new SolidBrush(on ? Color.White : Color.FromArgb(230, 230, 230)), box);
            if (on)
            {
                using (var pen = new Pen(Color.FromArgb(46, 125, 50), 2f))
                {
                    g.DrawLine(pen, box.X + 2, box.Y + chk * 0.5f, box.X + chk * 0.35f, box.Bottom - 2);
                    g.DrawLine(pen, box.X + chk * 0.35f, box.Bottom - 2, box.Right - 2, box.Y + 2);
                }
            }
            var txt = new RectangleF(box.Right + 6, r.Y, r.Width - chk - 18, r.Height);
            g.DrawString(text, GH_FontServer.Standard, new SolidBrush(on ? Color.White : Color.FromArgb(70, 70, 70)), txt,
                new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                if (_btnLabels.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Labels"); Comp.ShowLabels = !Comp.ShowLabels; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnCluster.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Cluster ID"); Comp.ShowCluster = !Comp.ShowCluster; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
            }
            return base.RespondToMouseDown(sender, e);
        }

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    #endregion

    public class GI_RadarAssembly : GH_Component
    {
        internal struct RadarLabelA { public Point3d Position; public string Text; public Vector3d XDir; public Vector3d YDir; }
        internal List<RadarLabelA> _axisLabels = new List<RadarLabelA>();
        internal List<RadarLabelA> _clusterLabels = new List<RadarLabelA>();
        internal double _textHeight = 0.15;
        internal Color _textColor = Color.Black;

        public bool ShowLabels { get; set; } = true;
        public bool ShowCluster { get; set; } = true;

        public GI_RadarAssembly()
          : base("GI_Radar (Assembly)", "GI_Radar_A",
              "Radar chart from Assembly. Visualises metric fingerprints.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override void CreateAttributes() { m_attributes = new RadarAssemblyAttributes(this); }

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
            if (ShowLabels)
                foreach (var lbl in _axisLabels)
                { Plane pl = new Plane(lbl.Position, lbl.XDir, lbl.YDir); args.Display.Draw3dText(lbl.Text, _textColor, pl, _textHeight, "Arial"); }
            if (ShowCluster)
                foreach (var lbl in _clusterLabels)
                { Plane pl = new Plane(lbl.Position, lbl.XDir, lbl.YDir); args.Display.Draw3dText(lbl.Text, _textColor, pl, _textHeight, "Arial"); }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_Auto6", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all (default).", GH_ParamAccess.list,-1);
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual(s). -1 = all (default).", GH_ParamAccess.list,-1);
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Show only top N best per cluster per generation. 0 = all (default). 1 = best one per cluster.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("X Spacing", "dX", "Horizontal spacing", GH_ParamAccess.item, 30.0);
            pManager.AddNumberParameter("Y Spacing", "dY", "Vertical spacing", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Radius", "R", "Axis length", GH_ParamAccess.item, 1.0);
            pManager.AddIntervalParameter("Metric Domains", "MDom",
                "Expected [min, max] domain per metric axis for normalization. Order matches assembly MetricNames. If not supplied, each axis uses observed max.", GH_ParamAccess.list);
            pManager.AddPointParameter("Insert Point", "Pt", "Base point", GH_ParamAccess.item, Point3d.Origin);
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Axes", "Axes", "Axis lines", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Polygon", "Poly", "Data polygons", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _axisLabels.Clear();
            _clusterLabels.Clear();

            GH_SGAssembly ghAssembly = null;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly required.");
                return;
            }
            var assembly = ghAssembly.Value;

            List<int> genList = new List<int>();
            List<int> indList = new List<int>();
            int topN = 0;
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            DA.GetData(3, ref topN);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));
            topN = Math.Max(0, topN);

            double xSpacing = 30.0, ySpacing = 10.0, radius = 1.0;
            Point3d insertPt = Point3d.Origin;
            var rawDomains = new List<GH_Interval>();
            DA.GetDataList(7, rawDomains);
            List<Interval> domains = new List<Interval>();
            DA.GetDataList(7, domains); // now domains contains only real Interval values
                                        // treat domains.Count == 0 as "no domains supplied"
            DA.GetData(4, ref xSpacing);
            DA.GetData(5, ref ySpacing);
            DA.GetData(6, ref radius);
            DA.GetData(8, ref insertPt);
            if (radius <= 0) radius = 1.0;

            var metricNames = assembly.MetricNames ?? new List<string>();
            if (metricNames.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly needs at least 2 metric names.");
                return;
            }

            int numAxes = metricNames.Count;
            bool hasDomains = domains != null && domains.Count >= numAxes;

            // Per-axis normalization: domains or observed max
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
                double[] axisMax = new double[numAxes];
                for (int m = 0; m < numAxes; m++) axisMax[m] = 1e-15;
                foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
                {
                    if (!allGens && !genList.Contains(gen.Generation)) continue;
                    for (int i = 0; i < (gen.Individuals?.Count ?? 0); i++)
                    {
                        if (!allInds && (indSet != null && !indSet.Contains(i))) continue;
                        var vals = gen.Individuals[i].AllMetrics();
                        if (vals.Count < numAxes) continue;
                        for (int m = 0; m < numAxes; m++)
                        {
                            double abs = Math.Abs(vals[m]);
                            if (!double.IsNaN(abs) && abs > axisMax[m]) axisMax[m] = abs;
                        }
                    }
                }
                for (int m = 0; m < numAxes; m++)
                {
                    axisMin[m] = 0;
                    axisRange[m] = axisMax[m] > 1e-15 ? axisMax[m] : 1.0;
                }
            }

            var axisDirs = new Vector3d[numAxes];
            for (int m = 0; m < numAxes; m++)
            {
                double ang = 2.0 * Math.PI * m / numAxes - Math.PI / 2.0;
                axisDirs[m] = new Vector3d(Math.Cos(ang), Math.Sin(ang), 0);
            }

            var axesTree = new GH_Structure<GH_Line>();
            var polyTree = new GH_Structure<GH_Curve>();
            int col = 0;
            int totalCharts = 0;

            // Build elite set when Top N > 0
            var eliteSet = new HashSet<(int gen, int ind)>();
            if (topN > 0)
            {
                foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
                {
                    if (!allGens && !genList.Contains(gen.Generation)) continue;
                    var candidates = new List<(int indIdx, double fit, int clust)>();
                    for (int i = 0; i < (gen.Individuals?.Count ?? 0); i++)
                    {
                        if (!allInds && (indSet != null && !indSet.Contains(i))) continue;
                        var ind = gen.Individuals[i];
                        if (ind == null || ind.Fitness >= double.MaxValue * 0.5) continue;
                        candidates.Add((i, ind.Fitness, ind.ClustGrp));
                    }
                    foreach (var grp in candidates.GroupBy(c => c.clust))
                    {
                        foreach (var x in grp.OrderBy(c => c.fit).Take(topN))
                            eliteSet.Add((gen.Generation, x.indIdx));
                    }
                }
            }

            foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
            {
                if (!allGens && !genList.Contains(gen.Generation)) continue;
                int row = 0;
                for (int indIdx = 0; indIdx < gen.Individuals.Count; indIdx++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(indIdx))) continue;
                    if (topN > 0 && !eliteSet.Contains((gen.Generation, indIdx))) continue;
                    var ind = gen.Individuals[indIdx];
                    var vals = ind.AllMetrics();
                    if (vals.Count < numAxes) continue;

                    Point3d center = new Point3d(
                        insertPt.X + col * xSpacing,
                        insertPt.Y - row * ySpacing,
                        insertPt.Z);
                    GH_Path outPath = new GH_Path(col, row);

                    var polygonPts = new List<Point3d>();
                    for (int m = 0; m < numAxes; m++)
                    {
                        axesTree.Append(new GH_Line(new Line(center, center + axisDirs[m] * radius)), outPath);
                        double norm = axisRange[m] > 0 ? (vals[m] - axisMin[m]) / axisRange[m] : 0;
                        double clampedNorm = double.IsNaN(vals[m]) ? 0 : Math.Max(0, Math.Min(1, norm));
                        polygonPts.Add(center + axisDirs[m] * (radius * clampedNorm));

                        Vector3d xdir = axisDirs[m];
                        Vector3d ydir = new Vector3d(-axisDirs[m].Y, axisDirs[m].X, 0);
                        // Left-side axes: flip so text extends outward (right) instead of inward
                        bool leftSide = axisDirs[m].X < -1e-10 || (Math.Abs(axisDirs[m].X) < 1e-10 && axisDirs[m].Y < 0);
                        if (leftSide) { xdir = -xdir; ydir = -ydir; }
                        // Place labels further outside to avoid overlap with polygon
                        double labelOffset = radius + _textHeight * 3.5;
                        Point3d labelPt = center + axisDirs[m] * labelOffset;
                        _axisLabels.Add(new RadarLabelA { Position = labelPt, Text = metricNames[m], XDir = xdir, YDir = ydir });
                        _axisLabels.Add(new RadarLabelA { Position = labelPt - ydir * _textHeight * 1.3, Text = string.Format("{0:F3} ({1:F2})", vals[m], clampedNorm), XDir = xdir, YDir = ydir });
                    }

                    if (polygonPts.Count >= 3)
                    {
                        polygonPts.Add(polygonPts[0]);
                        polyTree.Append(new GH_Curve(new Polyline(polygonPts).ToNurbsCurve()), outPath);
                    }

                    _clusterLabels.Add(new RadarLabelA
                    {
                        Position = center - new Vector3d(0, radius * 1.25, 0),
                        Text = string.Format("C{0} [G{1} I{2}]", ind.ClustGrp, gen.Generation, row),
                        XDir = Vector3d.XAxis,
                        YDir = Vector3d.YAxis
                    });

                    row++;
                    totalCharts++;
                }
                col++;
            }

            string normMode = hasDomains ? "user domains" : "observed-max";
            DA.SetDataTree(0, axesTree);
            DA.SetDataTree(1, polyTree);
            DA.SetData(2, string.Format("Radar from Assembly: {0} charts, {1} metrics. Normalization: {2}. Labels: {3}, Cluster: {4}",
                totalCharts, numAxes, normMode, ShowLabels ? "ON" : "OFF", ShowCluster ? "ON" : "OFF"));
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("B2C3D4E5-F6A7-8901-BCDE-F01234567891");
    }
}
