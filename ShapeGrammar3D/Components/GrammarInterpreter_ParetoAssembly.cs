using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    #region ParetoAssembly Attributes

    public class ParetoAssemblyAttributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds, _btnHighlight;
        private const float BTN_H = 22f, PAD = 4f, MIN_W = 180f;
        private GI_ParetoAssembly Comp => (GI_ParetoAssembly)Owner;

        public ParetoAssemblyAttributes(GI_ParetoAssembly owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            float w = Math.Max(Bounds.Width, MIN_W);
            float x = Bounds.X - (w - Bounds.Width) * 0.5f;
            float y = Bounds.Bottom + PAD * 2;
            _btnHighlight = new RectangleF(x + PAD, y, w - PAD * 2, BTN_H);
            _panelBounds = new RectangleF(x, Bounds.Bottom + PAD, w, y + BTN_H - Bounds.Bottom - PAD);
            Bounds = new RectangleF(x, Bounds.Y, w, y + BTN_H + PAD - Bounds.Y);
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
            string label = Comp.HighlightElite
                ? string.Format("Highlight Top {0}%", (Comp.CachedTopPercent * 100).ToString("F0"))
                : "Highlight Top %";
            DrawToggle(g, _btnHighlight, label, Comp.HighlightElite);
        }

        private void DrawToggle(Graphics g, RectangleF r, string text, bool on)
        {
            Color bg = on ? Color.FromArgb(230, 76, 175, 80) : Color.FromArgb(210, 200, 200, 200);
            Color border = on ? Color.FromArgb(56, 142, 60) : Color.FromArgb(165, 165, 165);
            using (var path = RoundRect(r, 4))
            {
                g.FillPath(new SolidBrush(bg), path);
                g.DrawPath(new Pen(border, 0.8f), path);
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
            if (e.Button == System.Windows.Forms.MouseButtons.Left && _btnHighlight.Contains(e.CanvasLocation))
            {
                Owner.RecordUndoEvent("Toggle Highlight Elite");
                Comp.HighlightElite = !Comp.HighlightElite;
                Owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
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

    /// <summary>
    /// Pareto front preview from SG Assembly. Same behaviour as GI_ParetoFront: axis system, ticks, grid, labels, Top % highlight.
    /// </summary>
    public class GI_ParetoAssembly : GH_Component
    {
        internal List<GraphLabel> _labels = new List<GraphLabel>();
        internal double _textHeight = 0.12;
        internal Color _textColor = Color.FromArgb(60, 60, 60);

        public bool HighlightElite { get; set; } = false;
        internal double CachedTopPercent { get; set; } = 0.05;

        public GI_ParetoAssembly()
          : base("GI_Pareto (Assembly)", "GI_Pareto_A",
              "Pareto front from Assembly. 1/2/3 objectives, axis system, Top % highlight. Filter by Generation and Individual (-1 = all).",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override void CreateAttributes() { m_attributes = new ParetoAssemblyAttributes(this); }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("HighlightElite", HighlightElite);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("HighlightElite")) HighlightElite = reader.GetBoolean("HighlightElite");
            return base.Read(reader);
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
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_FromSg", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Plane", "Pln", "Graph plane (origin = bottom-left)", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddIntegerParameter("Num Objectives", "nObj", "1=Disp vs Gen, 2=Disp vs Util, 3=Disp x Util x Feas", GH_ParamAccess.item, 1);
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all (default).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual(s). -1 = all (default).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Show only top N best per cluster per generation. 0 = all (default). 1 = best one per cluster.", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Clusters", "Cls", "Which cluster(s). -1 = all (default).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Width", "W", "Graph width", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Height", "H", "Graph height", GH_ParamAccess.item, 6.0);
            pManager.AddNumberParameter("Depth", "D", "Graph depth (3-obj only)", GH_ParamAccess.item, 6.0);
            pManager.AddNumberParameter("Text Height", "TxH", "Label text height", GH_ParamAccess.item, 0.12);
            pManager.AddNumberParameter("Point Size", "PtSz", "Base point radius", GH_ParamAccess.item, 0.08);
            pManager.AddNumberParameter("Top %", "Top%", "Top fraction to highlight (0.0–1.0). 0.05 = 5%.", GH_ParamAccess.item, 0.05);
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[9].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Ln", "Axis and grid lines", GH_ParamAccess.tree);
            pManager.AddColourParameter("Colours", "Col", "Colours for Lines", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
            pManager.AddPointParameter("Points All", "PtsAll", "All scatter points", GH_ParamAccess.tree);
            pManager.AddPointParameter("Points Top", "PtsTop", "Top/elite points only (when Highlight Top % ON)", GH_ParamAccess.tree);
            pManager.AddColourParameter("PointColours", "PCol", "Cluster colour per point (matches Points All)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("PointSizes", "PSz", "Radius per point", GH_ParamAccess.tree);
            pManager.AddPointParameter("LabelPts", "LblPt", "Axis label positions (for text)", GH_ParamAccess.tree);
            pManager.AddTextParameter("LabelTexts", "LblTxt", "Axis label texts (tick values, axis names, legend)", GH_ParamAccess.tree);
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

            Plane plane = Plane.WorldXY;
            int numObj = 1;
            List<int> genList = new List<int>();
            List<int> indList = new List<int>();
            List<int> clusterList = new List<int>();
            double graphW = 10.0, graphH = 6.0, graphD = 6.0;
            double textH = 0.12, ptSize = 0.08, topPercent = 0.05;

            DA.GetData(1, ref plane);
            DA.GetData(2, ref numObj);
            int topN = 0;
            DA.GetDataList(3, genList);
            DA.GetDataList(4, indList);
            DA.GetData(5, ref topN);
            DA.GetDataList(6, clusterList);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            if (clusterList.Count == 0) clusterList.Add(-1);
            DA.GetData(7, ref graphW);
            DA.GetData(8, ref graphH);
            DA.GetData(9, ref graphD);
            DA.GetData(10, ref textH);
            DA.GetData(11, ref ptSize);
            DA.GetData(12, ref topPercent);
            topN = Math.Max(0, topN);

            numObj = Math.Clamp(numObj, 1, 3);
            graphW = Math.Max(1.0, graphW);
            graphH = Math.Max(1.0, graphH);
            graphD = Math.Max(1.0, graphD);
            _textHeight = Math.Max(0.01, textH);
            ptSize = Math.Max(0.01, ptSize);
            topPercent = Math.Clamp(topPercent, 0.001, 1.0);
            CachedTopPercent = topPercent;

            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            bool allClusters = clusterList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            var gens = assembly.Generations ?? new List<AssemblyGeneration>();
            if (gens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                return;
            }

            var selectedGens = allGens ? gens.Select(g => g.Generation).OrderBy(g => g).ToList() : genList.Where(g => gens.Any(gn => gn.Generation == g)).Distinct().OrderBy(g => g).ToList();
            if (selectedGens.Count == 0) selectedGens.Add(gens[gens.Count - 1].Generation);

            var allRecords = new List<IndRec>();
            var allClusterIds = new SortedSet<int>();

            foreach (var gen in gens.Where(g => selectedGens.Contains(g.Generation)))
            {
                if (gen.Individuals == null) continue;
                for (int i = 0; i < gen.Individuals.Count; i++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(i))) continue;
                    var ind = gen.Individuals[i];
                    if (ind.Fitness >= double.MaxValue * 0.5 || double.IsInfinity(ind.Fitness)) continue;

                    allClusterIds.Add(ind.ClustGrp);
                    if (!allClusters && !clusterList.Contains(ind.ClustGrp)) continue;

                    allRecords.Add(new IndRec
                    {
                        Gen = gen.Generation,
                        Ind = i,
                        Fitness = ind.Fitness,
                        Util = ind.ObjUtil,
                        Feas = ind.ObjFeas,
                        Cluster = ind.ClustGrp,
                        Rank = ind.Rank
                    });
                }
            }

            // Filter to Top N per cluster per generation
            var records = topN > 0
                ? allRecords.GroupBy(r => (r.Gen, r.Cluster))
                    .SelectMany(grp => grp.OrderBy(r => r.Fitness).Take(topN))
                    .ToList()
                : allRecords;

            if (records.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No individuals match Generation/Individual/Cluster filters.");
                return;
            }

            var selectedClusters = allClusters ? allClusterIds.ToList() : clusterList.Where(c => allClusterIds.Contains(c)).ToList();
            if (selectedClusters.Count == 0) selectedClusters.Add(0);
            int totalClusters = allClusterIds.Count > 0 ? allClusterIds.Max() + 1 : 1;
            var clusterSet = new HashSet<int>(selectedClusters);
            records = records.Where(r => clusterSet.Contains(r.Cluster)).ToList();

            // Elite set (top % per cluster) – always computed for Points Top output
            var eliteSet = new HashSet<(int gen, int ind)>();
            int eliteCount = 0;
            {
                double fitMinE = records.Min(r => r.Fitness);
                double fitRngE = records.Max(r => r.Fitness) - fitMinE;
                double utilMinE = records.Min(r => r.Util);
                double utilRngE = records.Max(r => r.Util) - utilMinE;
                double feasMinE = records.Min(r => r.Feas);
                double feasRngE = records.Max(r => r.Feas) - feasMinE;
                if (fitRngE < 1e-12) fitRngE = 1; if (utilRngE < 1e-12) utilRngE = 1; if (feasRngE < 1e-12) feasRngE = 1;

                foreach (var grp in records.GroupBy(r => r.Cluster))
                {
                    var withDist = grp.Select(r =>
                    {
                        double dFit = (r.Fitness - fitMinE) / fitRngE;
                        double dUtil = (r.Util - utilMinE) / utilRngE;
                        double dFeas = (r.Feas - feasMinE) / feasRngE;
                        double dist = numObj == 1 ? dFit : (numObj == 2 ? Math.Sqrt(dFit * dFit + dUtil * dUtil) : Math.Sqrt(dFit * dFit + dUtil * dUtil + dFeas * dFeas));
                        return (Rec: r, Dist: dist);
                    }).OrderBy(x => x.Dist).ToList();
                    int keep = Math.Max(1, (int)Math.Ceiling(withDist.Count * topPercent));
                    for (int k = 0; k < keep && k < withDist.Count; k++)
                    {
                        eliteSet.Add((withDist[k].Rec.Gen, withDist[k].Rec.Ind));
                        eliteCount++;
                    }
                }
            }

            double fitMin = records.Min(r => r.Fitness);
            double fitMax = records.Max(r => r.Fitness);
            double utilMin = records.Min(r => r.Util);
            double utilMax = records.Max(r => r.Util);
            double feasMin = records.Min(r => r.Feas);
            double feasMax = records.Max(r => r.Feas);
            PadRange(ref fitMin, ref fitMax);
            if (numObj >= 2) PadRange(ref utilMin, ref utilMax);
            if (numObj >= 3) PadRange(ref feasMin, ref feasMax);

            int genMin = records.Min(r => r.Gen);
            int genMax = records.Max(r => r.Gen);
            if (genMin == genMax) genMax = genMin + 1;

            Point3d origin = plane.Origin;
            Vector3d xDir = plane.XAxis;
            Vector3d yDir = plane.YAxis;
            Vector3d zDir = plane.ZAxis;

            string xLabel = numObj == 1 ? "Generation" : "Displacement";
            string yLabel = numObj == 1 ? "Displacement" : "Avg Util Dev";
            string zLabel = numObj == 3 ? "Feasibility" : "";

            var linesTree = new GH_Structure<GH_Line>();
            var colorsTree = new GH_Structure<GH_Colour>();
            var pointsAllTree = new GH_Structure<GH_Point>();
            var pointsTopTree = new GH_Structure<GH_Point>();
            var ptColorsTree = new GH_Structure<GH_Colour>();
            var ptSizesTree = new GH_Structure<GH_Number>();

            GH_Path axisPath = new GH_Path(0);
            Color axisColor = Color.FromArgb(120, 120, 120);
            Color gridColor = Color.FromArgb(225, 225, 225);
            Color borderColor = Color.FromArgb(200, 200, 200);

            // Main axes
            linesTree.Append(new GH_Line(new Line(origin, origin + xDir * graphW)), axisPath);
            colorsTree.Append(new GH_Colour(axisColor), axisPath);
            linesTree.Append(new GH_Line(new Line(origin, origin + yDir * graphH)), axisPath);
            colorsTree.Append(new GH_Colour(axisColor), axisPath);
            Point3d topLeft = origin + yDir * graphH;
            Point3d topRight = topLeft + xDir * graphW;
            Point3d botRight = origin + xDir * graphW;
            linesTree.Append(new GH_Line(new Line(topLeft, topRight)), axisPath);
            colorsTree.Append(new GH_Colour(borderColor), axisPath);
            linesTree.Append(new GH_Line(new Line(botRight, topRight)), axisPath);
            colorsTree.Append(new GH_Colour(borderColor), axisPath);

            if (numObj == 3)
            {
                linesTree.Append(new GH_Line(new Line(origin, origin + zDir * graphD)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);
                Point3d topZ = origin + zDir * graphD;
                linesTree.Append(new GH_Line(new Line(topZ, topZ + xDir * graphW)), axisPath);
                colorsTree.Append(new GH_Colour(borderColor), axisPath);
                linesTree.Append(new GH_Line(new Line(topZ, topZ + yDir * graphH)), axisPath);
                colorsTree.Append(new GH_Colour(borderColor), axisPath);
                _labels.Add(new GraphLabel { Position = origin + zDir * (graphD * 0.5) - xDir * (_textHeight * 2) - yDir * (_textHeight * 2), Text = zLabel, XDir = zDir, YDir = xDir, Height = _textHeight, Color = _textColor });
            }

            int numTicks = 5;
            for (int t = 0; t <= numTicks; t++)
            {
                double frac = (double)t / numTicks;
                double xVal = numObj == 1 ? genMin + frac * (genMax - genMin) : fitMin + frac * (fitMax - fitMin);
                Point3d tickBase = origin + xDir * (frac * graphW);
                Point3d tickEnd = tickBase - yDir * (_textHeight * 0.3);
                linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);
                if (t > 0 && t < numTicks) { linesTree.Append(new GH_Line(new Line(tickBase, tickBase + yDir * graphH)), axisPath); colorsTree.Append(new GH_Colour(gridColor), axisPath); }
                _labels.Add(new GraphLabel { Position = tickEnd - yDir * (_textHeight * 1.2), Text = numObj == 1 ? ((int)Math.Round(xVal)).ToString() : FormatVal(xVal), XDir = xDir, YDir = yDir, Height = _textHeight * 0.8, Color = _textColor });
            }
            for (int t = 0; t <= numTicks; t++)
            {
                double frac = (double)t / numTicks;
                double yVal = numObj == 1 ? fitMin + frac * (fitMax - fitMin) : utilMin + frac * (utilMax - utilMin);
                Point3d tickBase = origin + yDir * (frac * graphH);
                Point3d tickEnd = tickBase - xDir * (_textHeight * 0.3);
                linesTree.Append(new GH_Line(new Line(tickBase, tickEnd)), axisPath);
                colorsTree.Append(new GH_Colour(axisColor), axisPath);
                if (t > 0 && t < numTicks) { linesTree.Append(new GH_Line(new Line(tickBase, tickBase + xDir * graphW)), axisPath); colorsTree.Append(new GH_Colour(gridColor), axisPath); }
                _labels.Add(new GraphLabel { Position = tickEnd - xDir * (_textHeight * 0.5), Text = FormatVal(yVal), XDir = xDir, YDir = yDir, Height = _textHeight * 0.8, Color = _textColor });
            }
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
                    if (t > 0 && t < numTicks) { linesTree.Append(new GH_Line(new Line(tickBase, tickBase + xDir * graphW)), axisPath); colorsTree.Append(new GH_Colour(gridColor), axisPath); }
                    _labels.Add(new GraphLabel { Position = tickEnd - xDir * (_textHeight * 0.5), Text = FormatVal(zVal), XDir = zDir, YDir = xDir, Height = _textHeight * 0.8, Color = _textColor });
                }
            }

            _labels.Add(new GraphLabel { Position = origin + xDir * (graphW * 0.5) - yDir * (_textHeight * 3.5), Text = xLabel, XDir = xDir, YDir = yDir, Height = _textHeight, Color = _textColor });
            _labels.Add(new GraphLabel { Position = origin - xDir * (_textHeight * 6) + yDir * (graphH * 0.5), Text = yLabel, XDir = yDir, YDir = -xDir, Height = _textHeight, Color = _textColor });

            bool hasRank = records.Any(r => r.Rank >= 0);
            int rank0Count = 0;

            foreach (var rec in records)
            {
                double xFrac = numObj == 1 ? (genMax > genMin ? (double)(rec.Gen - genMin) / (genMax - genMin) : 0.5) : (fitMax > fitMin ? (rec.Fitness - fitMin) / (fitMax - fitMin) : 0.5);
                double yFrac = numObj == 1 ? (fitMax > fitMin ? (rec.Fitness - fitMin) / (fitMax - fitMin) : 0.5) : (utilMax > utilMin ? (rec.Util - utilMin) / (utilMax - utilMin) : 0.5);
                double zFrac = numObj == 3 && feasMax > feasMin ? (rec.Feas - feasMin) / (feasMax - feasMin) : 0.5;
                xFrac = Math.Clamp(xFrac, 0, 1); yFrac = Math.Clamp(yFrac, 0, 1); zFrac = Math.Clamp(zFrac, 0, 1);

                Point3d pt = origin + xDir * (xFrac * graphW) + yDir * (yFrac * graphH);
                if (numObj == 3) pt += zDir * (zFrac * graphD);

                Color clColor = GetClusterColour(rec.Cluster, totalClusters);
                bool isRankZero = rec.Rank == 0;
                bool isElite = eliteSet.Contains((rec.Gen, rec.Ind));
                if (isRankZero) rank0Count++;
                double size = isElite ? ptSize * 2.5 : (isRankZero ? ptSize * 1.5 : ptSize);
                Color drawColor = (HighlightElite && !isElite) ? Color.FromArgb(80, clColor.R, clColor.G, clColor.B) : clColor;

                GH_Path ptPath = new GH_Path(rec.Gen);
                pointsAllTree.Append(new GH_Point(pt), ptPath);
                if (isElite)
                    pointsTopTree.Append(new GH_Point(pt), ptPath);
                ptColorsTree.Append(new GH_Colour(drawColor), ptPath);
                ptSizesTree.Append(new GH_Number(size), ptPath);

                double half = size * 0.5;
                GH_Path crossPath = new GH_Path(1, rec.Gen);
                linesTree.Append(new GH_Line(new Line(pt - xDir * half, pt + xDir * half)), crossPath);
                colorsTree.Append(new GH_Colour(drawColor), crossPath);
                linesTree.Append(new GH_Line(new Line(pt - yDir * half, pt + yDir * half)), crossPath);
                colorsTree.Append(new GH_Colour(drawColor), crossPath);
                if (numObj == 3) { linesTree.Append(new GH_Line(new Line(pt - zDir * half, pt + zDir * half)), crossPath); colorsTree.Append(new GH_Colour(drawColor), crossPath); }
            }

            foreach (int cl in selectedClusters)
            {
                Color clColor = GetClusterColour(cl, totalClusters);
                double legendY = graphH - selectedClusters.IndexOf(cl) * _textHeight * 1.8;
                Point3d legendPt = origin + xDir * (graphW + _textHeight * 1.5) + yDir * legendY;
                linesTree.Append(new GH_Line(new Line(legendPt - xDir * (_textHeight * 1.2), legendPt - xDir * (_textHeight * 0.2))), axisPath);
                colorsTree.Append(new GH_Colour(clColor), axisPath);
                _labels.Add(new GraphLabel { Position = legendPt, Text = string.Format("C{0}", cl), XDir = xDir, YDir = yDir, Height = _textHeight * 0.9, Color = clColor });
            }

            string genLabel = selectedGens.Count == 1 ? string.Format("Gen {0}", selectedGens[0]) : string.Format("Gens [{0}]", string.Join(",", selectedGens));
            string titleText = numObj == 1 ? "Displacement vs Generation" : (numObj == 2 ? "Pareto Front: Displacement vs Avg Util Dev" : "Pareto Front: Disp x Util x Feas");
            titleText += "  (" + genLabel + ")";
            _labels.Add(new GraphLabel { Position = origin + yDir * (graphH + _textHeight * 0.5), Text = titleText, XDir = xDir, YDir = yDir, Height = _textHeight * 1.2, Color = Color.FromArgb(40, 40, 40) });

            var labelPtsTree = new GH_Structure<GH_Point>();
            var labelTextsTree = new GH_Structure<GH_String>();
            GH_Path lblPath = new GH_Path(0);
            foreach (var lbl in _labels)
            {
                labelPtsTree.Append(new GH_Point(lbl.Position), lblPath);
                labelTextsTree.Append(new GH_String(lbl.Text), lblPath);
            }

            string highlightInfo = HighlightElite ? string.Format("Highlighted: {0} ({1}% per cluster)", eliteCount, (topPercent * 100).ToString("F0")) : "OFF";
            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, colorsTree);
            DA.SetData(2, string.Format("Pareto Assembly ({0} obj)\nGens: {1}\nIndividuals: {2}\nRank-0: {3}\nElite: {4}\nSize: {5:F1}x{6:F1}{7}",
                numObj, genLabel, records.Count, hasRank ? rank0Count.ToString() : "N/A", highlightInfo, graphW, graphH, numObj == 3 ? string.Format(" x {0:F1}", graphD) : ""));
            DA.SetDataTree(3, pointsAllTree);
            DA.SetDataTree(4, pointsTopTree);
            DA.SetDataTree(5, ptColorsTree);
            DA.SetDataTree(6, ptSizesTree);
            DA.SetDataTree(7, labelPtsTree);
            DA.SetDataTree(8, labelTextsTree);
        }

        private static void PadRange(ref double min, ref double max) { if (max <= min) max = min + 1; double r = max - min; min -= r * 0.05; max += r * 0.05; }
        private static string FormatVal(double v) { double abs = Math.Abs(v); if (abs >= 1000) return v.ToString("F0"); if (abs >= 1) return v.ToString("F2"); if (abs >= 0.01) return v.ToString("F4"); return v.ToString("E2"); }
        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0, 1);
            int r = 0, g, b;
            if (t <= 0.5) { g = (int)((t / 0.5) * 255); b = 255; } else { g = 255; b = (int)((1 - (t - 0.5) / 0.5) * 255); }
            return Color.FromArgb(r, Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        private struct IndRec { public int Gen, Ind, Cluster, Rank; public double Fitness, Util, Feas; }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("D4E5F6A7-B8C9-0123-DEF0-123456789013");
    }
}
