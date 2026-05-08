using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    #region FeasibilityPreview Attributes

    public class FeasibilityPreviewAttributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds;
        private RectangleF _btnUtil, _btnLength, _btnLengthBins, _btnAngle, _btnIntersect, _btnDangling, _btnBoundary;
        private const float BTN_H = 22f, PAD = 4f, MIN_W = 200f;
        private GI_FeasibilityPreview Comp => (GI_FeasibilityPreview)Owner;

        public FeasibilityPreviewAttributes(GI_FeasibilityPreview owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            float w = Math.Max(Bounds.Width, MIN_W);
            float x = Bounds.X - (w - Bounds.Width) * 0.5f;
            float y = Bounds.Bottom + PAD * 2;
            float cx = x + PAD, cw = w - PAD * 2;

            _btnUtil = new RectangleF(cx, y, cw, BTN_H); y += BTN_H + PAD;
            _btnLength = new RectangleF(cx, y, cw, BTN_H); y += BTN_H + PAD;
            _btnLengthBins = new RectangleF(cx, y, cw, BTN_H); y += BTN_H + PAD;
            _btnAngle = new RectangleF(cx, y, cw, BTN_H); y += BTN_H + PAD;
            _btnIntersect = new RectangleF(cx, y, cw, BTN_H); y += BTN_H + PAD;
            _btnDangling = new RectangleF(cx, y, cw, BTN_H); y += BTN_H + PAD;
            _btnBoundary = new RectangleF(cx, y, cw, BTN_H);
            _panelBounds = new RectangleF(x, Bounds.Bottom + PAD, w, y + BTN_H + PAD - Bounds.Bottom - PAD);
            Bounds = new RectangleF(x, Bounds.Y, w, y + BTN_H + PAD - Bounds.Y);
        }

        protected override void Render(GH_Canvas canvas, Graphics g, GH_CanvasChannel channel)
        {
            base.Render(canvas, g, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            g.SmoothingMode = SmoothingMode.HighQuality;
            using (var path = RoundRect(_panelBounds, 5))
            {
                g.FillPath(new SolidBrush(Color.FromArgb(220, 245, 245, 245)), path);
                g.DrawPath(new Pen(Color.FromArgb(140, 160, 160, 160), 0.8f), path);
            }
            DrawToggle(g, _btnUtil, "Utilization", Comp.ShowUtilization);
            DrawToggle(g, _btnLength, "Length (ranked)", Comp.ShowLength);
            DrawToggle(g, _btnLengthBins, "Length bins (VRepet)", Comp.ShowLengthBins);
            DrawToggle(g, _btnAngle, "Angle (nodes)", Comp.ShowAngle);
            DrawToggle(g, _btnIntersect, "Intersections", Comp.ShowIntersection);
            DrawToggle(g, _btnDangling, "Dangling", Comp.ShowDangling);
            DrawToggle(g, _btnBoundary, "Boundary (VBoundary)", Comp.ShowBoundary);
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
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                if (_btnUtil.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Utilization"); Comp.ShowUtilization = !Comp.ShowUtilization; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnLength.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Length"); Comp.ShowLength = !Comp.ShowLength; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnLengthBins.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Length Bins"); Comp.ShowLengthBins = !Comp.ShowLengthBins; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnAngle.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Angle"); Comp.ShowAngle = !Comp.ShowAngle; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnIntersect.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Intersection"); Comp.ShowIntersection = !Comp.ShowIntersection; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnDangling.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Dangling"); Comp.ShowDangling = !Comp.ShowDangling; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnBoundary.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Boundary"); Comp.ShowBoundary = !Comp.ShowBoundary; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
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
    /// Feasibility metrics preview: element length ranking, node angles, intersections, dangling elements.
    /// Toggle buttons for Utilization, Length, Angle, Intersection, Dangling. Outputs feasibility scores per individual.
    /// </summary>
    public class GI_FeasibilityPreview : GH_Component
    {
        private struct DotMark { public Point3d Position; public Color Colour; }
        private struct BoundaryDisplayItem { public Mesh Mesh; public Color WireColour; }
        private List<DotMark> _angleDots = new List<DotMark>();
        private List<DotMark> _intersectDots = new List<DotMark>();
        private List<DotMark> _danglingDots = new List<DotMark>();
        private List<BoundaryDisplayItem> _boundaryItems = new List<BoundaryDisplayItem>();
        private double _dotRadius = 0.15;

        public bool ShowUtilization { get; set; } = true;
        public bool ShowLength { get; set; } = false;
        public bool ShowLengthBins { get; set; } = false;
        public bool ShowAngle { get; set; } = false;
        public bool ShowIntersection { get; set; } = false;
        public bool ShowDangling { get; set; } = false;
        public bool ShowBoundary { get; set; } = false;

        public GI_FeasibilityPreview()
          : base("GI_Feasibility (Assembly)", "GI_Feas",
              "Feasibility metrics preview: length ranking, node angles, intersections, dangling. Toggle buttons for each overlay.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override void CreateAttributes() { m_attributes = new FeasibilityPreviewAttributes(this); }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("ShowUtilization", ShowUtilization);
            writer.SetBoolean("ShowLength", ShowLength);
            writer.SetBoolean("ShowLengthBins", ShowLengthBins);
            writer.SetBoolean("ShowAngle", ShowAngle);
            writer.SetBoolean("ShowIntersection", ShowIntersection);
            writer.SetBoolean("ShowDangling", ShowDangling);
            writer.SetBoolean("ShowBoundary", ShowBoundary);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("ShowUtilization")) ShowUtilization = reader.GetBoolean("ShowUtilization");
            if (reader.ItemExists("ShowLength")) ShowLength = reader.GetBoolean("ShowLength");
            if (reader.ItemExists("ShowLengthBins")) ShowLengthBins = reader.GetBoolean("ShowLengthBins");
            if (reader.ItemExists("ShowAngle")) ShowAngle = reader.GetBoolean("ShowAngle");
            if (reader.ItemExists("ShowIntersection")) ShowIntersection = reader.GetBoolean("ShowIntersection");
            if (reader.ItemExists("ShowDangling")) ShowDangling = reader.GetBoolean("ShowDangling");
            if (reader.ItemExists("ShowBoundary")) ShowBoundary = reader.GetBoolean("ShowBoundary");
            return base.Read(reader);
        }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            float r = (float)_dotRadius;
            if (ShowAngle && _angleDots != null)
                foreach (var d in _angleDots)
                    args.Display.DrawSphere(new Sphere(d.Position, r), d.Colour);
            if (ShowIntersection && _intersectDots != null)
                foreach (var d in _intersectDots)
                    args.Display.DrawSphere(new Sphere(d.Position, r), d.Colour);
            if (ShowDangling && _danglingDots != null)
                foreach (var d in _danglingDots)
                    args.Display.DrawSphere(new Sphere(d.Position, r), d.Colour);
            if (ShowBoundary && _boundaryItems != null)
                foreach (var b in _boundaryItems)
                {
                    if (b.Mesh == null) continue;
                    args.Display.DrawMeshWires(b.Mesh, b.WireColour, 1);
                }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_FromSg", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all.", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual(s). -1 = all.", GH_ParamAccess.list, -1);
            pManager.AddNumberParameter("X Spacing", "dX", "Horizontal spacing between individuals", GH_ParamAccess.item, 30.0);
            pManager.AddNumberParameter("Y Spacing", "dY", "Vertical spacing between individuals", GH_ParamAccess.item, 10.0);
            pManager.AddPointParameter("Insert Point", "Pt", "Base point for layout", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddIntegerParameter("Load Case", "LC", "Load case for utilization (-1 = last)", GH_ParamAccess.item, -1);
            pManager.AddNumberParameter("Dot Radius", "DotR", "Radius of angle/intersection/dangling dots", GH_ParamAccess.item, 0.15);
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional: orient layout (XY through Insert Pt) onto this plane.",
                GH_ParamAccess.item);
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Lines", "Element lines (offset by layout)", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Meshes", "Meshes", "Coloured meshes (util or length)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VDang", "VDang", "Dangling penalty per individual", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VAng", "VAng", "Angle penalty per individual", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VLen", "VLen", "Length penalty per individual", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VIntersect", "VInt", "Intersection penalty per individual", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VRepet", "VRepet", "Repetitiveness penalty per individual (10% length bins; lower = more similar elements)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VDup", "VDup", "Duplicate-element penalty per individual (goal: 0; Rule 051 can produce duplicates)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Feas Total", "Feas", "Total feasibility per individual", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
            pManager.AddPointParameter("Angle Node Pts", "AngPts", "Points at each node with angle data (for text tags). Layout offset applied.", GH_ParamAccess.tree);
            pManager.AddTextParameter("Angle Node Txt", "AngTxt", "Angle feasibility per node: min angle [°] and classification (good/medium/bad). One item per angle node.", GH_ParamAccess.tree);
            pManager.AddPointParameter("Label Pt", "LabelPt", "One point per structure for placing a feasibility summary text tag.", GH_ParamAccess.tree);
            pManager.AddTextParameter("Label Txt", "LabelTxt", "Feasibility summary text per structure (Feas, VAng, angle node counts).", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Intersection Count", "IntN", "Number of qualifying intersections (Bar–Bar, Strut–Bar) per individual.", GH_ParamAccess.tree);
            pManager.AddPointParameter("Intersection Pts", "IntPts", "Actual intersection locations (Point3d). One point per intersection.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Intersection Values", "IntVal", "Penalty value per intersection contributing to VIntersect.", GH_ParamAccess.tree);
            pManager.AddTextParameter("Elem Type Counts", "TypeCnt", "Per structure: MainBeam, Strut, Bar, Other counts (for reverse-engineering when bar marker is lost).", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VBoundary", "VBnd", "Length-weighted ratio of element length lying outside the boundary Brep/Mesh stored on the shape (0 = fully inside, 1 = fully outside). One value per individual.", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Boundary Mesh", "BndMesh", "Boundary geometry (Brep tessellated to Mesh, or BoundaryMesh as-is) per individual, offset to layout position. One mesh per individual that has a boundary.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Outside Ratios", "OutR", "Per-element outside ratio (0..1) for each line in the same order as Lines. Useful for inspecting which beams violate the boundary.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _angleDots.Clear();
            _intersectDots.Clear();
            _danglingDots.Clear();
            _boundaryItems.Clear();

            GH_SGAssembly ghAssembly = null;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly required.");
                return;
            }
            var assembly = ghAssembly.Value;

            List<int> genList = new List<int>(), indList = new List<int>();
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            double xSpacing = 30, ySpacing = 10;
            Point3d insertPt = Point3d.Origin;
            int lcIndex = -1;
            double dotR = 0.15;
            double lenShort = 0.5, lenOptLo = 1.0, lenOptHi = 5.0, lenLong = 12.0;
            double angMin = 10, angOpt = 30;
            int topN = 0;

            DA.GetData(3, ref xSpacing);
            DA.GetData(4, ref ySpacing);
            DA.GetData(5, ref insertPt);
            DA.GetData(6, ref lcIndex);
            DA.GetData(7, ref dotR);
            _dotRadius = Math.Max(0.01, dotR);

            Transform dispXf = PreviewLayoutTransforms.GetOptionalDisplayTransform(DA, 8, insertPt);

            if (assembly.Config != null)
            {
                if (assembly.Config.FeasibilityAngleMinDeg.HasValue) angMin = assembly.Config.FeasibilityAngleMinDeg.Value;
                if (assembly.Config.FeasibilityAngleOptDeg.HasValue) angOpt = assembly.Config.FeasibilityAngleOptDeg.Value;
                if (assembly.Config.FeasibilityLenTooShort.HasValue) lenShort = assembly.Config.FeasibilityLenTooShort.Value;
                if (assembly.Config.FeasibilityLenOptLow.HasValue) lenOptLo = assembly.Config.FeasibilityLenOptLow.Value;
                if (assembly.Config.FeasibilityLenOptHigh.HasValue) lenOptHi = assembly.Config.FeasibilityLenOptHigh.Value;
                if (assembly.Config.FeasibilityLenTooLong.HasValue) lenLong = assembly.Config.FeasibilityLenTooLong.Value;
            }

            var feasSettings = FeasibilitySettings.Default();
            feasSettings.LenTooShort = lenShort;
            feasSettings.LenOptLow = lenOptLo;
            feasSettings.LenOptHigh = lenOptHi;
            feasSettings.LenTooLong = lenLong;
            feasSettings.AngleMinDeg = angMin;
            feasSettings.AngleOptDeg = angOpt;

            var eliteSet = new HashSet<(int gen, int ind)>();
            if (topN > 0)
            {
                var candidates = new List<(int gen, int ind, double fit, int clust)>();
                foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
                {
                    if (!allGens && !genList.Contains(gen.Generation)) continue;
                    for (int i = 0; i < (gen.Individuals?.Count ?? 0); i++)
                    {
                        if (!allInds && (indSet == null || !indSet.Contains(i))) continue;
                        var ind = gen.Individuals[i];
                        if (ind == null || ind.Fitness >= double.MaxValue * 0.5) continue;
                        candidates.Add((gen.Generation, i, ind.Fitness, ind.ClustGrp));
                    }
                }
                foreach (var grp in candidates.GroupBy(c => (c.gen, c.clust)))
                {
                    var ordered = grp.OrderBy(c => c.fit).ToList();
                    int keep = Math.Min(topN, ordered.Count);
                    for (int k = 0; k < keep; k++)
                        eliteSet.Add((ordered[k].gen, ordered[k].ind));
                }
            }

            var lineTree = new GH_Structure<GH_Line>();
            var meshTree = new GH_Structure<GH_Mesh>();
            var vDangTree = new GH_Structure<GH_Number>();
            var vAngTree = new GH_Structure<GH_Number>();
            var vLenTree = new GH_Structure<GH_Number>();
            var vIntTree = new GH_Structure<GH_Number>();
            var vRepetTree = new GH_Structure<GH_Number>();
            var vDupTree = new GH_Structure<GH_Number>();
            var feasTree = new GH_Structure<GH_Number>();
            var angleNodePtsTree = new GH_Structure<GH_Point>();
            var angleNodeTxtTree = new GH_Structure<GH_String>();
            var labelPtTree = new GH_Structure<GH_Point>();
            var labelTxtTree = new GH_Structure<GH_String>();
            var intCountTree = new GH_Structure<GH_Integer>();
            var intPtsTree = new GH_Structure<GH_Point>();
            var intValTree = new GH_Structure<GH_Number>();
            var elemTypeCountsTree = new GH_Structure<GH_String>();
            var vBoundaryTree = new GH_Structure<GH_Number>();
            var boundaryMeshTree = new GH_Structure<GH_Mesh>();
            var outsideRatioTree = new GH_Structure<GH_Number>();

            double[] utilThresholds = new double[] { 50, 80, 95, 100, 110 };
            int col = 0;
            int totalCount = 0;
            int boundaryStructureCount = 0;
            // Cache: tessellated mesh per boundary geometry instance (Brep or Mesh).
            // Many individuals typically share the same boundary, so meshing once is much cheaper.
            var boundaryMeshCache = new Dictionary<object, Mesh>();

            foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
            {
                if (!allGens && !genList.Contains(gen.Generation)) continue;
                int row = 0;
                for (int indIdx = 0; indIdx < (gen.Individuals?.Count ?? 0); indIdx++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(indIdx))) continue;
                    if (topN > 0 && !eliteSet.Contains((gen.Generation, indIdx))) continue;
                    var ind = gen.Individuals[indIdx];
                    SG_Shape shape = ind?.Shape;
                    TB_Model model = ind?.Model;
                    if (shape == null || shape.Elems == null)
                    {
                        row++;
                        continue;
                    }

                    shape.RegisterElemsToNodes();

                    // Refresh boundary violation from current geometry so VBoundary in
                    // the preview always reflects the actual shape, not a stale cache
                    // from the GA run. Also collect per-element outside ratios for
                    // visualization and the OutR output.
                    Dictionary<SG_Elem1D, double> elemOutsideRatio = null;
                    bool hasBoundary = shape.BoundaryBrep != null || shape.BoundaryMesh != null;
                    if (hasBoundary)
                    {
                        double surfTol = BoundaryConstraintUtil.DefaultSurfaceTol(shape.BoundaryBrep, shape.BoundaryMesh);
                        elemOutsideRatio = new Dictionary<SG_Elem1D, double>();
                        double totalLen = 0.0, outsideLen = 0.0;
                        foreach (var el in shape.Elems)
                        {
                            if (!(el is SG_Elem1D e1d)) continue;
                            if (e1d.Nodes == null || e1d.Nodes.Length < 2 || e1d.Nodes[0] == null || e1d.Nodes[1] == null) continue;
                            Line ln0 = new Line(e1d.Nodes[0].Pt, e1d.Nodes[1].Pt);
                            if (!ln0.IsValid || ln0.Length < 1e-9) { elemOutsideRatio[e1d] = 0.0; continue; }
                            double rOut = BoundaryConstraintUtil.OutsideRatio(ln0, shape.BoundaryBrep, shape.BoundaryMesh, surfTol);
                            elemOutsideRatio[e1d] = rOut;
                            totalLen += ln0.Length;
                            outsideLen += ln0.Length * rOut;
                        }
                        shape.BoundaryViolationRatio = totalLen > 1e-12 ? outsideLen / totalLen : 0.0;
                        boundaryStructureCount++;
                    }

                    FeasibilityResult feas = FeasibilityMetrics.Compute(shape, feasSettings);

                    Vector3d offset = new Vector3d(
                        insertPt.X + col * xSpacing,
                        insertPt.Y - row * ySpacing,
                        insertPt.Z);

                    GH_Path path = new GH_Path(col, row);

                    vDangTree.Append(new GH_Number(feas.VDang), path);
                    vAngTree.Append(new GH_Number(feas.VAng), path);
                    vLenTree.Append(new GH_Number(feas.VLen), path);
                    vIntTree.Append(new GH_Number(feas.VIntersect), path);
                    vRepetTree.Append(new GH_Number(feas.VRepet), path);
                    vDupTree.Append(new GH_Number(feas.VDup), path);
                    vBoundaryTree.Append(new GH_Number(feas.VBoundary), path);
                    feasTree.Append(new GH_Number(feas.TotalViolation), path);

                    bool useLengthBinsColor = ShowLengthBins;
                    bool useLengthColor = ShowLength && !useLengthBinsColor;
                    bool useUtilColor = ShowUtilization && !useLengthColor && !useLengthBinsColor && model != null;
                    bool useBoundaryColor = ShowBoundary && !useUtilColor && !useLengthColor && !useLengthBinsColor && elemOutsideRatio != null;

                    int lc = ResolveLoadCase(model, lcIndex);

                    var lenData = useLengthColor ? FeasibilityMetrics.GetElementLengthData(shape, lenShort, lenOptLo, lenOptHi, lenLong) : null;
                    var sortedLen = useLengthColor && lenData != null
                        ? lenData.OrderByDescending(x => x.Length).ToList()
                        : null;
                    var binMapping = useLengthBinsColor ? FeasibilityMetrics.GetElementLengthBinMapping(shape, 10.0) : (default, 0);
                    var elemToBin = useLengthBinsColor && binMapping.Mappings != null
                        ? binMapping.Mappings.ToDictionary(x => x.Elem, x => (x.BinIndex, binMapping.TotalBinCount))
                        : null;

                    foreach (var elem in shape.Elems)
                    {
                        if (!(elem is SG_Elem1D e1)) continue;
                        Line ln = GetElementLine(e1);
                        if (!ln.IsValid) continue;
                        Line offsetLn = new Line(ln.From + offset, ln.To + offset);
                        lineTree.Append(new GH_Line(offsetLn), path);

                        TB_Element_1D modelElem = FindModelElementByLine(model, ln);

                        double outsideR = 0.0;
                        if (elemOutsideRatio != null && elemOutsideRatio.TryGetValue(e1, out var orVal)) outsideR = orVal;
                        outsideRatioTree.Append(new GH_Number(outsideR), path);

                        Color meshColor = Color.Gray;
                        if (useLengthBinsColor && elemToBin != null && elemToBin.TryGetValue(e1, out var binInfo))
                        {
                            meshColor = BinIndexToColor(binInfo.BinIndex, binInfo.TotalBinCount);
                        }
                        else if (useLengthColor && sortedLen != null)
                        {
                            var ld = sortedLen.FirstOrDefault(x => x.Elem == e1);
                            meshColor = FeasColor(ld.Classification);
                        }
                        else if (useUtilColor && modelElem != null)
                        {
                            double util = ComputeUtilization(model, modelElem, lc);
                            meshColor = UtilColor(util, utilThresholds);
                        }
                        else if (useBoundaryColor)
                        {
                            meshColor = BoundaryColor(outsideR);
                        }

                        if (ShowLength || ShowUtilization || ShowLengthBins || ShowBoundary)
                        {
                            double sw = 0.05, sh = 0.05;
                            if (modelElem?.Sec != null)
                            {
                                sw = Math.Sqrt(Math.Max(0.01, modelElem.Sec.Area)) * 0.0005;
                                sh = sw;
                            }
                            if ((sw <= 0 || sh <= 0) && e1.CrossSection != null)
                            {
                                if (e1.CrossSection is SH_CrossSection_RHS rhs) { sw = rhs.Width * 0.0005; sh = rhs.Height * 0.0005; }
                                else if (e1.CrossSection is SH_CrossSection_Beam b) { sw = Math.Sqrt(b.Area) * 0.001; sh = sw; }
                            }
                            if (sw > 0 && sh > 0)
                            {
                                Mesh m = ExtrudeRect(offsetLn, sw, sh, Color.FromArgb(180, meshColor));
                                if (m != null) meshTree.Append(new GH_Mesh(m), path);
                            }
                        }
                    }

                    var allNodeAngleData = FeasibilityMetrics.GetAllNodesAngleData(shape, angMin, angOpt);
                    if (ShowAngle)
                    {
                        foreach (var (Node, MinAngleDeg, Classification) in allNodeAngleData)
                        {
                            Point3d pt = Node.Pt + offset;
                            Color dotColor = Classification >= 0 ? FeasColor(Classification) : Color.FromArgb(180, 160, 160, 160);
                            _angleDots.Add(new DotMark { Position = pt, Colour = dotColor });
                        }
                    }
                    foreach (var (Node, MinAngleDeg, Classification) in allNodeAngleData)
                    {
                        angleNodePtsTree.Append(new GH_Point(Node.Pt + offset), path);
                        string txt;
                        if (Classification < 0)
                            txt = "—";
                        else
                        {
                            string clsStr = Classification == FeasibilityMetrics.CLS_GOOD ? "good" : (Classification == FeasibilityMetrics.CLS_ORANGE ? "medium" : "bad");
                            txt = string.Format("{0:F1}° ({1})", MinAngleDeg, clsStr);
                        }
                        angleNodeTxtTree.Append(new GH_String(txt), path);
                    }
                    Point3d labelPt;
                    if (shape.Nodes != null && shape.Nodes.Count > 0)
                    {
                        var cen = Point3d.Origin;
                        foreach (var n in shape.Nodes) cen += n.Pt;
                        cen /= shape.Nodes.Count;
                        labelPt = cen + offset;
                    }
                    else
                        labelPt = insertPt + offset;
                    int angGood = allNodeAngleData.Count(x => x.Classification == FeasibilityMetrics.CLS_GOOD);
                    int angOrange = allNodeAngleData.Count(x => x.Classification == FeasibilityMetrics.CLS_ORANGE);
                    int angBad = allNodeAngleData.Count(x => x.Classification == FeasibilityMetrics.CLS_BAD);
                    string labelTxt = string.Format("Feas:{0:F3} VAng:{1:F3} VBnd:{2:F3} | Angle nodes: {3} good, {4} medium, {5} bad",
                        feas.TotalViolation, feas.VAng, feas.VBoundary, angGood, angOrange, angBad);
                    labelPtTree.Append(new GH_Point(labelPt), path);
                    labelTxtTree.Append(new GH_String(labelTxt), path);

                    // Boundary geometry visualization (per individual). Tessellate Brep
                    // (or take Mesh as-is) once per unique boundary instance, then offset
                    // a duplicate to the layout slot for this individual.
                    if (hasBoundary)
                    {
                        Mesh baseMesh = GetOrTessellateBoundary(shape.BoundaryBrep, shape.BoundaryMesh, boundaryMeshCache);
                        if (baseMesh != null)
                        {
                            Mesh placed = baseMesh.DuplicateMesh();
                            placed.Translate(offset);
                            boundaryMeshTree.Append(new GH_Mesh(placed), path);
                            if (ShowBoundary)
                            {
                                Color wireColor = shape.BoundaryViolationRatio > 1e-6
                                    ? Color.FromArgb(220, 50, 50)
                                    : Color.FromArgb(80, 140, 200);
                                _boundaryItems.Add(new BoundaryDisplayItem { Mesh = placed, WireColour = wireColor });
                            }
                        }
                    }
                    var typeCounts = FeasibilityMetrics.GetElementTypeCounts(shape);
                    elemTypeCountsTree.Append(new GH_String(string.Format("MainBeam:{0} Strut:{1} Bar:{2} Other:{3}", typeCounts.MainBeam, typeCounts.Strut, typeCounts.Bar, typeCounts.Other)), path);
                    var intersectionData = FeasibilityMetrics.GetIntersectionData(shape);
                    intCountTree.Append(new GH_Integer(intersectionData.Count), path);
                    foreach (var d in intersectionData)
                    {
                        Point3d ptLayout = d.Point + offset;
                        intPtsTree.Append(new GH_Point(ptLayout), path);
                        intValTree.Append(new GH_Number(d.Value), path);
                        if (ShowIntersection)
                            _intersectDots.Add(new DotMark { Position = ptLayout, Colour = Color.FromArgb(255, 200, 0) });
                    }
                    if (ShowDangling)
                    {
                        foreach (var (_, MidPoint) in FeasibilityMetrics.GetDanglingElementMidpoints(shape))
                            _danglingDots.Add(new DotMark { Position = MidPoint + offset, Colour = Color.FromArgb(255, 50, 50) });
                    }

                    totalCount++;
                    row++;
                }
                col++;
            }

            if (!dispXf.IsIdentity)
            {
                lineTree = PreviewLayoutTransforms.TransformLineTree(lineTree, dispXf);
                meshTree = PreviewLayoutTransforms.TransformMeshTree(meshTree, dispXf);
                angleNodePtsTree = PreviewLayoutTransforms.TransformPointTree(angleNodePtsTree, dispXf);
                labelPtTree = PreviewLayoutTransforms.TransformPointTree(labelPtTree, dispXf);
                intPtsTree = PreviewLayoutTransforms.TransformPointTree(intPtsTree, dispXf);
                boundaryMeshTree = PreviewLayoutTransforms.TransformMeshTree(boundaryMeshTree, dispXf);
                for (int i = 0; i < _angleDots.Count; i++)
                {
                    var d = _angleDots[i];
                    Point3d p = d.Position;
                    p.Transform(dispXf);
                    _angleDots[i] = new DotMark { Position = p, Colour = d.Colour };
                }
                for (int i = 0; i < _intersectDots.Count; i++)
                {
                    var d = _intersectDots[i];
                    Point3d p = d.Position;
                    p.Transform(dispXf);
                    _intersectDots[i] = new DotMark { Position = p, Colour = d.Colour };
                }
                for (int i = 0; i < _danglingDots.Count; i++)
                {
                    var d = _danglingDots[i];
                    Point3d p = d.Position;
                    p.Transform(dispXf);
                    _danglingDots[i] = new DotMark { Position = p, Colour = d.Colour };
                }
                foreach (var b in _boundaryItems)
                {
                    if (b.Mesh != null)
                        b.Mesh.Transform(dispXf);
                }
            }

            DA.SetDataTree(0, lineTree);
            DA.SetDataTree(1, meshTree);
            DA.SetDataTree(2, vDangTree);
            DA.SetDataTree(3, vAngTree);
            DA.SetDataTree(4, vLenTree);
            DA.SetDataTree(5, vIntTree);
            DA.SetDataTree(6, vRepetTree);
            DA.SetDataTree(7, vDupTree);
            DA.SetDataTree(8, feasTree);
            DA.SetData(9, string.Format("Feasibility Preview: {0} individuals ({1} with boundary). Util:{2} Len:{3} LenBins:{4} Ang:{5} Int:{6} Dang:{7} Bnd:{8} Repet+Dup:computed",
                totalCount, boundaryStructureCount,
                ShowUtilization ? "ON" : "OFF", ShowLength ? "ON" : "OFF", ShowLengthBins ? "ON" : "OFF",
                ShowAngle ? "ON" : "OFF", ShowIntersection ? "ON" : "OFF", ShowDangling ? "ON" : "OFF",
                ShowBoundary ? "ON" : "OFF"));
            DA.SetDataTree(10, angleNodePtsTree);
            DA.SetDataTree(11, angleNodeTxtTree);
            DA.SetDataTree(12, labelPtTree);
            DA.SetDataTree(13, labelTxtTree);
            DA.SetDataTree(14, intCountTree);
            DA.SetDataTree(15, intPtsTree);
            DA.SetDataTree(16, intValTree);
            DA.SetDataTree(17, elemTypeCountsTree);
            DA.SetDataTree(18, vBoundaryTree);
            DA.SetDataTree(19, boundaryMeshTree);
            DA.SetDataTree(20, outsideRatioTree);
        }

        /// <summary>
        /// Tessellates a boundary Brep to a Mesh (cached by reference), or returns the
        /// supplied Mesh as-is. Used so we mesh each unique boundary only once even when
        /// many individuals share it.
        /// </summary>
        private static Mesh GetOrTessellateBoundary(Brep brep, Mesh mesh, Dictionary<object, Mesh> cache)
        {
            if (brep != null)
            {
                if (cache.TryGetValue(brep, out var cached)) return cached;
                Mesh result = null;
                try
                {
                    var meshes = Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
                    if (meshes != null && meshes.Length > 0)
                    {
                        result = new Mesh();
                        foreach (var m in meshes) if (m != null) result.Append(m);
                        if (!result.IsValid) result = null;
                    }
                }
                catch { result = null; }
                cache[brep] = result;
                return result;
            }
            if (mesh != null)
            {
                if (cache.TryGetValue(mesh, out var cached)) return cached;
                cache[mesh] = mesh;
                return mesh;
            }
            return null;
        }

        /// <summary>Green (fully inside) → orange (partial) → red (fully outside).</summary>
        private static Color BoundaryColor(double outsideRatio)
        {
            outsideRatio = Math.Clamp(outsideRatio, 0.0, 1.0);
            if (outsideRatio <= 1e-6) return Color.FromArgb(0, 180, 80);
            if (outsideRatio < 0.5) return Lerp(Color.FromArgb(0, 180, 80), Color.FromArgb(255, 165, 0), outsideRatio / 0.5);
            return Lerp(Color.FromArgb(255, 165, 0), Color.FromArgb(220, 50, 50), (outsideRatio - 0.5) / 0.5);
        }

        private static Color FeasColor(int cls)
        {
            if (cls == FeasibilityMetrics.CLS_GOOD) return Color.FromArgb(0, 180, 80);
            if (cls == FeasibilityMetrics.CLS_ORANGE) return Color.FromArgb(255, 165, 0);
            return Color.FromArgb(220, 50, 50);
        }

        /// <summary>Full 0–255 gradient: blue→cyan→green→yellow→red for length-bin visualization.</summary>
        private static Color BinIndexToColor(int binIndex, int totalBins)
        {
            if (totalBins <= 1) return Color.FromArgb(0, 128, 255);
            double t = (double)binIndex / Math.Max(1, totalBins - 1);
            t = Math.Clamp(t, 0, 1);
            var stops = new[] {
                (0.00, Color.FromArgb(0, 0, 255)),
                (0.25, Color.FromArgb(0, 255, 255)),
                (0.50, Color.FromArgb(0, 255, 0)),
                (0.75, Color.FromArgb(255, 255, 0)),
                (1.00, Color.FromArgb(255, 0, 0))
            };
            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (t >= stops[i].Item1 && t <= stops[i + 1].Item1)
                {
                    double local = (t - stops[i].Item1) / (stops[i + 1].Item1 - stops[i].Item1);
                    return Lerp(stops[i].Item2, stops[i + 1].Item2, local);
                }
            }
            return stops[stops.Length - 1].Item2;
        }

        private static Color UtilColor(double util, double[] t)
        {
            double pct = util * 100;
            if (pct <= t[0]) return Color.FromArgb(30, 100, 255);
            if (pct <= t[1]) return Lerp(Color.FromArgb(30, 100, 255), Color.FromArgb(0, 200, 80), (pct - t[0]) / (t[1] - t[0]));
            if (pct <= t[2]) return Color.FromArgb(0, 200, 80);
            if (pct <= t[3]) return Lerp(Color.FromArgb(0, 200, 80), Color.FromArgb(255, 220, 0), (pct - t[2]) / (t[3] - t[2]));
            if (pct <= t[4]) return Lerp(Color.FromArgb(255, 220, 0), Color.FromArgb(220, 30, 30), (pct - t[3]) / (t[4] - t[3]));
            return Color.FromArgb(220, 30, 30);
        }

        private static Color Lerp(Color a, Color b, double f)
        {
            f = Math.Clamp(f, 0, 1);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * f),
                (int)(a.G + (b.G - a.G) * f),
                (int)(a.B + (b.B - a.B) * f));
        }

        private static double ComputeUtilization(TB_Model model, TB_Element_1D e, int lcIdx)
        {
            if (model == null || e == null || e.Sec == null) return 0;
            double gM0 = 1.0;
            double N_Rd = e.Sec.Area * e.Sec.Mat.Fy / gM0;
            double My_Rd = e.Sec.Wy * e.Sec.Mat.Fy / gM0 * 1e-3;
            double Mz_Rd = e.Sec.Wz * e.Sec.Mat.Fy / gM0 * 1e-3;
            if (N_Rd <= 0 || My_Rd <= 0 || Mz_Rd <= 0) return 0;
            if (model.LCs == null || lcIdx < 0) return 0;
            int id = Math.Min(lcIdx, model.LCs.Length - 1);
            if (id < 0) return 0;
            double[] F = e.Calc_Forces(id);
            double N_Ed = Math.Max(Math.Abs(F[0]), Math.Abs(F[6]));
            double My_Ed = Math.Max(Math.Abs(F[4]), Math.Abs(F[10]));
            double Mz_Ed = Math.Max(Math.Abs(F[5]), Math.Abs(F[11]));
            return N_Ed / N_Rd + My_Ed / My_Rd + Mz_Ed / Mz_Rd;
        }

        private const double LINE_MATCH_TOL = 1e-3;

        /// <summary>Gets the segment line for display. Prefers node positions so rule-02 columns (which have Crv overwritten with beam curve) still draw correctly.</summary>
        private static Line GetElementLine(SG_Elem1D e1)
        {
            if (e1?.Nodes != null && e1.Nodes.Length >= 2 && e1.Nodes[0] != null && e1.Nodes[1] != null)
                return new Line(e1.Nodes[0].Pt, e1.Nodes[1].Pt);
            if (e1.Crv != null)
                return new Line(e1.Crv.PointAtStart, e1.Crv.PointAtEnd);
            return e1.Ln;
        }

        private static TB_Element_1D FindModelElementByLine(TB_Model model, Line ln)
        {
            if (model?.Elem1Ds == null || !ln.IsValid) return null;
            foreach (var e in model.Elem1Ds)
            {
                if (e?.Line == null) continue;
                if (e.Line.From.DistanceTo(ln.From) <= LINE_MATCH_TOL && e.Line.To.DistanceTo(ln.To) <= LINE_MATCH_TOL) return e;
                if (e.Line.From.DistanceTo(ln.To) <= LINE_MATCH_TOL && e.Line.To.DistanceTo(ln.From) <= LINE_MATCH_TOL) return e;
            }
            return null;
        }

        private static int ResolveLoadCase(TB_Model model, int requested)
        {
            if (model?.Nodes == null) return -1;
            var first = model.Nodes.FirstOrDefault(n => n.Disps != null && n.Disps.Count > 0);
            if (first == null) return -1;
            int num = first.Disps.Count;
            return (requested < 0 || requested >= num) ? num - 1 : requested;
        }

        private static Mesh ExtrudeRect(Line axis, double wM, double hM, Color faceColour)
        {
            if (wM <= 0 || hM <= 0 || axis.Length < 1e-12) return null;
            double hw = wM * 0.5, hh = hM * 0.5;
            Vector3d t = axis.UnitTangent;
            Vector3d ly = Math.Abs(t * Vector3d.ZAxis) > 0.99 ? Vector3d.YAxis : Vector3d.CrossProduct(Vector3d.ZAxis, t);
            ly.Unitize();
            Vector3d lz = Vector3d.CrossProduct(t, ly);
            lz.Unitize();
            var mesh = new Mesh();
            for (int end = 0; end < 2; end++)
            {
                Point3d o = end == 0 ? axis.From : axis.To;
                mesh.Vertices.Add(o - ly * hw - lz * hh);
                mesh.Vertices.Add(o + ly * hw - lz * hh);
                mesh.Vertices.Add(o + ly * hw + lz * hh);
                mesh.Vertices.Add(o - ly * hw + lz * hh);
                for (int i = 0; i < 4; i++) mesh.VertexColors.Add(faceColour);
            }
            int[][] faces = { new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 }, new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 } };
            foreach (var f in faces)
                mesh.Faces.AddFace(f[0], f[1], f[2], f[3]);
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;
        public override Guid ComponentGuid => new Guid("B1C2D3E4-F5A6-4B7C-8D9E-0F1A2B3C4D5E");
    }
}
