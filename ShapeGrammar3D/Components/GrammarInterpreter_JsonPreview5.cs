using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

#pragma warning disable CS0618 // Archived component is intentionally referenced by its custom attributes.

namespace ShapeGrammar3D.Components
{
    #region Custom Attributes

    public class JsonPreview5Attributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds;
        private RectangleF _btnMesh;
        private RectangleF _btnDeform;
        private RectangleF _btnLabel;
        private RectangleF _sliderFont;
        private RectangleF _sliderScale;

        private bool _draggingFont;
        private bool _draggingScale;

        private const float BTN_H = 22f;
        private const float SLD_H = 26f;
        private const float PAD = 4f;
        private const float MIN_W = 200f;

        private GrammarInterpreter_JsonPreview5 Comp
            => (GrammarInterpreter_JsonPreview5)Owner;

        public JsonPreview5Attributes(GrammarInterpreter_JsonPreview5 owner)
            : base(owner) { }

        protected override void Layout()
        {
            base.Layout();

            RectangleF std = Bounds;
            float w = Math.Max(std.Width, MIN_W);
            float xShift = (w - std.Width) * 0.5f;
            float x = std.X - xShift;

            float y = std.Bottom + PAD * 2;
            float cw = w - PAD * 2;
            float cx = x + PAD;

            _btnMesh = new RectangleF(cx, y, cw, BTN_H);
            y += BTN_H + PAD;
            _btnDeform = new RectangleF(cx, y, cw, BTN_H);
            y += BTN_H + PAD;
            _btnLabel = new RectangleF(cx, y, cw, BTN_H);
            y += BTN_H + PAD * 2;

            _sliderFont = new RectangleF(cx, y, cw, SLD_H);
            y += SLD_H + PAD;
            _sliderScale = new RectangleF(cx, y, cw, SLD_H);
            y += SLD_H + PAD;

            _panelBounds = new RectangleF(x, std.Bottom + PAD, w, y - std.Bottom - PAD);
            Bounds = new RectangleF(x, std.Y, w, y - std.Y);
        }

        protected override void Render(GH_Canvas canvas, Graphics g, GH_CanvasChannel channel)
        {
            base.Render(canvas, g, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.HighQuality;

            DrawPanel(g);
            DrawToggle(g, _btnMesh, "Section Mesh", Comp.ShowSectionMesh);
            DrawToggle(g, _btnDeform, "Deformation", Comp.ShowDeformation);
            DrawToggle(g, _btnLabel, "Deform. Label", Comp.ShowDeformLabel);

            float sepY = _sliderFont.Y - PAD;
            using (var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 0.8f))
                g.DrawLine(pen, _sliderFont.X, sepY, _sliderFont.Right, sepY);

            DrawSlider(g, _sliderFont, "Font", Comp.FontSize,
                GrammarInterpreter_JsonPreview5.FONT_MIN,
                GrammarInterpreter_JsonPreview5.FONT_MAX, false);
            DrawSlider(g, _sliderScale, "Scale", Comp.DeformationScale,
                GrammarInterpreter_JsonPreview5.SCALE_MIN,
                GrammarInterpreter_JsonPreview5.SCALE_MAX, true);

            g.SmoothingMode = prev;
        }

        private void DrawPanel(Graphics g)
        {
            using (var path = RoundRect(_panelBounds, 5))
            {
                using (var fill = new SolidBrush(Color.FromArgb(220, 245, 245, 245)))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Color.FromArgb(140, 160, 160, 160), 0.8f))
                    g.DrawPath(pen, path);
            }
        }

        private void DrawToggle(Graphics g, RectangleF r, string text, bool on)
        {
            Color bg = on ? Color.FromArgb(230, 76, 175, 80) : Color.FromArgb(210, 200, 200, 200);
            Color border = on ? Color.FromArgb(56, 142, 60) : Color.FromArgb(165, 165, 165);
            Color fg = on ? Color.White : Color.FromArgb(70, 70, 70);

            using (var path = RoundRect(r, 4))
            {
                using (var fill = new SolidBrush(bg))
                    g.FillPath(fill, path);
                using (var pen = new Pen(border, 0.8f))
                    g.DrawPath(pen, path);
            }

            float chk = 13f;
            float cy = r.Y + (r.Height - chk) / 2f;
            RectangleF box = new RectangleF(r.X + 6, cy, chk, chk);

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

        private void DrawSlider(Graphics g, RectangleF r, string label,
            double value, double min, double max, bool intMode)
        {
            float labelW = 40f;
            float valueW = 46f;
            float trackH = 6f;

            var (tX, tW) = TrackGeom(r);
            float tY = r.Y + r.Height / 2f - trackH / 2f;

            using (var font = (Font)GH_FontServer.Small.Clone())
            using (var brush = new SolidBrush(Color.FromArgb(90, 90, 90)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, brush, new RectangleF(r.X + 3, r.Y, labelW, r.Height), sf);

                sf.Alignment = StringAlignment.Far;
                string vTxt = intMode ? ((int)Math.Round(value)).ToString() : value.ToString("F1");
                g.DrawString(vTxt, font, brush, new RectangleF(r.Right - valueW - 2, r.Y, valueW, r.Height), sf);
            }

            RectangleF trackRect = new RectangleF(tX, tY, tW, trackH);
            using (var path = RoundRect(trackRect, 3))
            using (var fill = new SolidBrush(Color.FromArgb(210, 210, 210)))
                g.FillPath(fill, path);

            double t = max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;
            float fillW = (float)(tW * t);
            if (fillW > 1)
            {
                RectangleF fillRect = new RectangleF(tX, tY, fillW, trackH);
                using (var path = RoundRect(fillRect, 3))
                using (var fill = new SolidBrush(Color.FromArgb(230, 66, 133, 244)))
                    g.FillPath(fill, path);
            }

            float thumbR = 7f;
            float thumbX = tX + fillW;
            RectangleF thumb = new RectangleF(
                thumbX - thumbR, r.Y + r.Height / 2f - thumbR, thumbR * 2, thumbR * 2);
            using (var fill = new SolidBrush(Color.White))
                g.FillEllipse(fill, thumb);
            using (var pen = new Pen(Color.FromArgb(66, 133, 244), 1.5f))
                g.DrawEllipse(pen, thumb);
        }

        private (float x, float w) TrackGeom(RectangleF r)
        {
            float labelW = 40f;
            float valueW = 46f;
            float mx = 6f;
            float x = r.X + labelW + mx;
            float w = r.Width - labelW - valueW - mx * 2;
            return (x, w);
        }

        private double ValueFromMouse(RectangleF slider, float mouseX, double min, double max, bool snap)
        {
            var (tX, tW) = TrackGeom(slider);
            double t = Math.Clamp((mouseX - tX) / tW, 0, 1);
            double raw = min + t * (max - min);
            return snap ? Math.Round(raw) : Math.Round(raw * 2) / 2.0;
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            PointF pt = e.CanvasLocation;

            if (_btnMesh.Contains(pt))
            {
                Owner.RecordUndoEvent("Toggle Section Mesh");
                Comp.ShowSectionMesh = !Comp.ShowSectionMesh;
                Owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }
            if (_btnDeform.Contains(pt))
            {
                Owner.RecordUndoEvent("Toggle Deformation");
                Comp.ShowDeformation = !Comp.ShowDeformation;
                Owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }
            if (_btnLabel.Contains(pt))
            {
                Owner.RecordUndoEvent("Toggle Deform Label");
                Comp.ShowDeformLabel = !Comp.ShowDeformLabel;
                Owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            if (_sliderFont.Contains(pt))
            {
                _draggingFont = true;
                Comp.FontSize = ValueFromMouse(_sliderFont, pt.X,
                    GrammarInterpreter_JsonPreview5.FONT_MIN,
                    GrammarInterpreter_JsonPreview5.FONT_MAX, false);
                sender.Invalidate();
                return GH_ObjectResponse.Capture;
            }
            if (_sliderScale.Contains(pt))
            {
                _draggingScale = true;
                Comp.DeformationScale = ValueFromMouse(_sliderScale, pt.X,
                    GrammarInterpreter_JsonPreview5.SCALE_MIN,
                    GrammarInterpreter_JsonPreview5.SCALE_MAX, true);
                sender.Invalidate();
                return GH_ObjectResponse.Capture;
            }

            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_draggingFont)
            {
                Comp.FontSize = ValueFromMouse(_sliderFont, e.CanvasLocation.X,
                    GrammarInterpreter_JsonPreview5.FONT_MIN,
                    GrammarInterpreter_JsonPreview5.FONT_MAX, false);
                sender.Invalidate();
                return GH_ObjectResponse.Ignore;
            }
            if (_draggingScale)
            {
                Comp.DeformationScale = ValueFromMouse(_sliderScale, e.CanvasLocation.X,
                    GrammarInterpreter_JsonPreview5.SCALE_MIN,
                    GrammarInterpreter_JsonPreview5.SCALE_MAX, true);
                sender.Invalidate();
                return GH_ObjectResponse.Ignore;
            }
            return base.RespondToMouseMove(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_draggingFont || _draggingScale)
            {
                bool wasFont = _draggingFont;
                _draggingFont = false;
                _draggingScale = false;
                Owner.RecordUndoEvent(wasFont ? "Font Size" : "Deformation Scale");
                Owner.ExpireSolution(true);
                return GH_ObjectResponse.Release;
            }
            return base.RespondToMouseUp(sender, e);
        }

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            float d = rad * 2;
            var p = new GraphicsPath();
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    #endregion

    #region Component
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
    
    public class GrammarInterpreter_JsonPreview5 : GH_Component
    {
        public bool ShowSectionMesh { get; set; }
        public bool ShowDeformation { get; set; } = true;
        public bool ShowDeformLabel { get; set; } = true;
        public double FontSize { get; set; } = 14.0;
        public double DeformationScale { get; set; } = 100.0;

        public const double FONT_MIN = 4.0;
        public const double FONT_MAX = 48.0;
        public const double SCALE_MIN = 0.0;
        public const double SCALE_MAX = 1000.0;

        public GrammarInterpreter_JsonPreview5()
          : base("GI_JsonPreview5", "GI_JPrev5",
              "Reads GA4 JSON and previews structures with embedded visualization controls " +
              "(section mesh, deformation, labels, font size, deformation scale)",
              UT.CAT, UT.GR_INT)
        {
        }

        public override void CreateAttributes()
        {
            m_attributes = new JsonPreview5Attributes(this);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("ShowSectionMesh", ShowSectionMesh);
            writer.SetBoolean("ShowDeformation", ShowDeformation);
            writer.SetBoolean("ShowDeformLabel", ShowDeformLabel);
            writer.SetDouble("FontSize", FontSize);
            writer.SetDouble("DeformationScale", DeformationScale);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("ShowSectionMesh")) ShowSectionMesh = reader.GetBoolean("ShowSectionMesh");
            if (reader.ItemExists("ShowDeformation")) ShowDeformation = reader.GetBoolean("ShowDeformation");
            if (reader.ItemExists("ShowDeformLabel")) ShowDeformLabel = reader.GetBoolean("ShowDeformLabel");
            if (reader.ItemExists("FontSize")) FontSize = reader.GetDouble("FontSize");
            if (reader.ItemExists("DeformationScale")) DeformationScale = reader.GetDouble("DeformationScale");
            return base.Read(reader);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("JSON Path", "JSON",
                "Path to GA run JSON file", GH_ParamAccess.item);                              // 0
            pManager.AddGenericParameter("SG_Shape", "SG_Shape",
                "Initial SG Assembly", GH_ParamAccess.item);                                   // 1
            pManager.AddGenericParameter("Automatic Rules", "Autorules",
                "Rules used in the GA run", GH_ParamAccess.list);                              // 2
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices (-1 = all)", GH_ParamAccess.list);                         // 3
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices (-1 = all)", GH_ParamAccess.list);                         // 4
            pManager.AddNumberParameter("Top %", "Top%",
                "Show best % per generation (0-1)", GH_ParamAccess.item, 1.0);                // 5
            pManager.AddIntegerParameter("Load Case", "LC",
                "Load case index (-1 = last)", GH_ParamAccess.item, -1);                       // 6
            pManager.AddBooleanParameter("CroSec Opt", "CSOpt",
                "Apply cross-section optimization", GH_ParamAccess.item, false);               // 7
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between generation columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);          // 8
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between individual rows. Default (0, 0, -30).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingWide);         // 9
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each cell's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);                                                         // 10

            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Lines",
                "Element lines {col;row}(element)", GH_ParamAccess.tree);                      // 0
            pManager.AddMeshParameter("Meshes", "Meshes",
                "Section extrusion meshes {col;row}(element)", GH_ParamAccess.tree);           // 1
            pManager.AddLineParameter("Deformed", "Def",
                "Deformed element lines {col;row}(element)", GH_ParamAccess.tree);             // 2
            pManager.AddNumberParameter("MaxDisp", "MaxD",
                "Max displacement per individual {col;row}", GH_ParamAccess.tree);             // 3
            pManager.AddTextParameter("Labels", "Labels",
                "Deformation value labels at max disp node {col;row}", GH_ParamAccess.tree);   // 4
            pManager.AddPointParameter("LabelPts", "LabelPts",
                "Label anchor points {col;row}", GH_ParamAccess.tree);                         // 5
            pManager.AddNumberParameter("FontSize", "FSize",
                "Current font size from embedded slider", GH_ParamAccess.item);                // 6
            pManager.AddGenericParameter("Shapes", "Shapes",
                "Reconstructed shapes {col;row}", GH_ParamAccess.tree);                        // 7
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models",
                "Reconstructed models {col;row}", GH_ParamAccess.tree);                        // 8
            pManager.AddTextParameter("Info", "Info",
                "Preview summary", GH_ParamAccess.item);                                       // 9
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string jsonPath = string.Empty;
            SG_Shape iniShape = new SG_Shape();
            List<SG_Rule> rules = new List<SG_Rule>();

            if (!DA.GetData(0, ref jsonPath)) return;
            if (!DA.GetData(1, ref iniShape)) return;
            if (!DA.GetDataList(2, rules)) return;

            var genList = new List<int>();
            DA.GetDataList(3, genList);
            var indList = new List<int>();
            DA.GetDataList(4, indList);

            double topPct = 1.0;
            int loadCase = -1;
            bool croSecOpt = false;
            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingWide;

            DA.GetData(5, ref topPct);
            DA.GetData(6, ref loadCase);
            DA.GetData(7, ref croSecOpt);
            DA.GetData(8, ref colSpacing);
            DA.GetData(9, ref rowSpacing);
            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 10);
            topPct = Math.Clamp(topPct, 0, 1);

            if (!File.Exists(jsonPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    string.Format("File not found: {0}", jsonPath));
                return;
            }

            GARunStore store;
            try { store = GARunStore.LoadFromJson(jsonPath); }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            if (store.Generations == null || store.Generations.Count == 0)
            {
                DA.SetData(9, "No generations in file.");
                return;
            }

            var lookup = new Dictionary<int, GenerationRecord>();
            foreach (var gr in store.Generations) lookup[gr.Generation] = gr;

            if (genList.Count == 0 || genList.Contains(-1))
                genList = lookup.Keys.OrderBy(k => k).ToList();

            bool allInd = indList.Count == 0 || indList.Contains(-1);
            var indSet = new HashSet<int>(indList);

            var columns = new List<List<Entry>>();
            int totalFailed = 0;

            foreach (int gIdx in genList)
            {
                var col = new List<Entry>();
                if (!lookup.TryGetValue(gIdx, out var genRec) || genRec.Individuals == null)
                {
                    columns.Add(col);
                    continue;
                }

                for (int i = 0; i < genRec.Individuals.Count; i++)
                {
                    if (!allInd && !indSet.Contains(i)) continue;
                    var rec = genRec.Individuals[i];

                    SG_Shape shape = null;
                    TB_Model model = null;
                    try
                    {
                        var gt = new SG_Genotype(
                            new List<int>(rec.Chromosome),
                            new List<double>(rec.ChromosomeParam));
                        shape = iniShape.DeepCopy();
                        for (int j = 0; j < rules.Count; j++)
                            rules[j].RuleOperation(ref shape, ref gt);
                        shape.RegisterElemsToNodes();
                        model = new TB_Model(shape);
                        var slv = new SolveLS(ref model);
                        model = slv.Mdl;
                        if (croSecOpt) model = OptimizeCrossSections(model);
                    }
                    catch
                    {
                        totalFailed++;
                        continue;
                    }
                    if (shape == null) continue;
                    col.Add(new Entry { Shape = shape, Model = model, Rec = rec, Gen = gIdx, Ind = i });
                }

                if (topPct < 1.0 && col.Count > 0)
                {
                    col.Sort((a, b) => a.Rec.Fitness.CompareTo(b.Rec.Fitness));
                    int keep = Math.Max(1, (int)Math.Ceiling(col.Count * topPct));
                    if (keep < col.Count) col = col.GetRange(0, keep);
                }
                columns.Add(col);
            }

            int total = columns.Sum(c => c.Count);
            if (total == 0)
            {
                DA.SetData(9, "No shapes reconstructed.");
                return;
            }

            var linesT = new GH_Structure<GH_Line>();
            var meshT = new GH_Structure<GH_Mesh>();
            var defT = new GH_Structure<GH_Line>();
            var maxDT = new GH_Structure<GH_Number>();
            var labT = new GH_Structure<GH_String>();
            var labPtT = new GH_Structure<GH_Point>();
            var shpT = new GH_Structure<GH_ObjectWrapper>();
            var mdlT = new GH_Structure<GH_TB_Model>();

            double scale = DeformationScale;
            int maxRows = 0;

            for (int c = 0; c < columns.Count; c++)
            {
                var col = columns[c];
                if (col.Count > maxRows) maxRows = col.Count;

                for (int r = 0; r < col.Count; r++)
                {
                    var e = col[r];
                    var off = c * colSpacing + r * rowSpacing;
                    Point3d cellOrigin = Point3d.Origin + off;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform3D(displayPlane, cellOrigin);
                    var path = new GH_Path(c, r);

                    shpT.Append(new GH_ObjectWrapper(e.Shape), path);
                    if (e.Model != null) mdlT.Append(new GH_TB_Model(e.Model), path);

                    if (e.Shape.Elems == null) continue;

                    int eIdx = 0;
                    foreach (var elem in e.Shape.Elems)
                    {
                        if (!(elem is SG_Elem1D e1d)) continue;
                        Line ln = new Line(e1d.Ln.From + off, e1d.Ln.To + off);
                        ln.Transform(cellXf);
                        linesT.Append(new GH_Line(ln), path);

                        if (ShowSectionMesh)
                        {
                            double sw = 0, sh = 0;
                            if (e.Model != null && e.Model.Elem1Ds != null
                                && eIdx < e.Model.Elem1Ds.Count)
                            {
                                var tbSec = e.Model.Elem1Ds[eIdx].Sec;
                                if (tbSec is Section_RHS rhs5)
                                { sw = rhs5.W; sh = rhs5.H; }
                                else if (tbSec is Section_Rect rect)
                                { sw = rect.B; sh = rect.H; }
                            }
                            else if (e1d.CrossSection is SH_CrossSection_RHS shRhs5)
                            {
                                sw = shRhs5.Width; sh = shRhs5.Height;
                            }
                            else if (e1d.CrossSection is SH_CrossSection_Rectangle sr)
                            {
                                sw = sr.width; sh = sr.height;
                            }
                            Mesh m = ExtrudeRect(ln, sw * 0.001, sh * 0.001);
                            if (m != null) meshT.Append(new GH_Mesh(m), path);
                        }
                        eIdx++;
                    }

                    if ((ShowDeformation || ShowDeformLabel)
                        && e.Model != null && e.Model.Nodes != null && e.Model.Nodes.Count > 0)
                    {
                        int lc = ResolveLc(e.Model, loadCase);
                        double maxD = 0;
                        Point3d maxPt = Point3d.Origin;

                        var defPts = new Dictionary<int, Point3d>();
                        foreach (var nd in e.Model.Nodes)
                        {
                            if (!nd.Id.HasValue) continue;
                            Point3d pt = nd.Pt + off;
                            double mag = 0;
                            if (nd.Disps != null && lc < nd.Disps.Count && nd.Disps[lc] != null)
                            {
                                double[] d = nd.Disps[lc];
                                if (d.Length >= 3)
                                {
                                    var dv = new Vector3d(d[0], d[1], d[2]);
                                    mag = dv.Length;
                                    pt += dv * scale;
                                }
                            }
                            pt.Transform(cellXf);
                            defPts[nd.Id.Value] = pt;
                            if (mag > maxD) { maxD = mag; maxPt = pt; }
                        }

                        if (ShowDeformation && e.Model.Elem1Ds != null)
                        {
                            foreach (var tb in e.Model.Elem1Ds)
                            {
                                if (tb.Nodes == null || tb.Nodes.Count < 2) continue;
                                if (!tb.Nodes[0].Id.HasValue || !tb.Nodes[1].Id.HasValue) continue;
                                int a = tb.Nodes[0].Id.Value, b = tb.Nodes[1].Id.Value;
                                if (defPts.ContainsKey(a) && defPts.ContainsKey(b))
                                    defT.Append(new GH_Line(new Line(defPts[a], defPts[b])), path);
                            }
                        }

                        maxDT.Append(new GH_Number(maxD), path);

                        if (ShowDeformLabel)
                        {
                            string label = string.Format("{0:E3}", maxD);
                            labT.Append(new GH_String(label), path);
                            labPtT.Append(new GH_Point(maxPt), path);
                        }
                    }
                }
            }

            string info = string.Format(
                "JSON: {0}\nRun: {1} | Obj: {2}\n" +
                "Structures: {3} (failed: {4})\n" +
                "Layout: {5} cols x {6} rows\n" +
                "Section Mesh: {7} | Deformation: {8} | Label: {9}\n" +
                "Font: {10:F1} | Scale: {11}",
                Path.GetFileName(jsonPath), store.RunId, store.NumObjectives,
                total, totalFailed,
                columns.Count, maxRows,
                ShowSectionMesh ? "ON" : "OFF",
                ShowDeformation ? "ON" : "OFF",
                ShowDeformLabel ? "ON" : "OFF",
                FontSize, (int)DeformationScale);

            DA.SetDataTree(0, linesT);
            DA.SetDataTree(1, meshT);
            DA.SetDataTree(2, defT);
            DA.SetDataTree(3, maxDT);
            DA.SetDataTree(4, labT);
            DA.SetDataTree(5, labPtT);
            DA.SetData(6, FontSize);
            DA.SetDataTree(7, shpT);
            DA.SetDataTree(8, mdlT);
            DA.SetData(9, info);
        }

        #region Helpers

        private static int ResolveLc(TB_Model m, int req)
        {
            if (m.Nodes == null || m.Nodes.Count == 0) return 0;
            int mx = m.Nodes.Max(n => n.Disps != null ? n.Disps.Count : 0);
            if (mx == 0) return 0;
            return (req < 0 || req >= mx) ? mx - 1 : req;
        }

        private static Mesh ExtrudeRect(Line axis, double wM, double hM)
        {
            if (wM <= 0 || hM <= 0 || axis.Length < 1e-12) return null;

            double hw = wM * 0.5, hh = hM * 0.5;
            Vector3d t = axis.UnitTangent;
            Vector3d ly = Math.Abs(t * Vector3d.ZAxis) > 0.99
                ? Vector3d.YAxis
                : Vector3d.CrossProduct(Vector3d.ZAxis, t);
            ly.Unitize();
            Vector3d lz = Vector3d.CrossProduct(t, ly);
            lz.Unitize();

            var pts = new Point3d[8];
            for (int end = 0; end < 2; end++)
            {
                Point3d o = end == 0 ? axis.From : axis.To;
                pts[end * 4 + 0] = o - ly * hw - lz * hh;
                pts[end * 4 + 1] = o + ly * hw - lz * hh;
                pts[end * 4 + 2] = o + ly * hw + lz * hh;
                pts[end * 4 + 3] = o - ly * hw + lz * hh;
            }

            var mesh = new Mesh();
            mesh.Vertices.AddVertices(pts);
            mesh.Faces.AddFace(0, 1, 5, 4);
            mesh.Faces.AddFace(1, 2, 6, 5);
            mesh.Faces.AddFace(2, 3, 7, 6);
            mesh.Faces.AddFace(3, 0, 4, 7);
            mesh.Faces.AddFace(0, 3, 2, 1);
            mesh.Faces.AddFace(4, 5, 6, 7);
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private static TB_Model OptimizeCrossSections(TB_Model solved)
        {
            if (solved == null || solved.Elem1Ds == null || solved.Elem1Ds.Count == 0)
                return solved;

            const int N = 20;
            const double STEP = 50.0;
            int cnt = solved.Elem1Ds.Count;
            int[] idx = new int[cnt];

            TB_Model cur = RebuildSections(solved, idx, STEP);
            var slv = new SolveLS(ref cur); cur = slv.Mdl;

            for (int it = 0; it < N; it++)
            {
                bool any = false;
                for (int ei = 0; ei < cur.Elem1Ds.Count; ei++)
                {
                    if (idx[ei] >= N - 1) continue;
                    if (ElemUtil(cur, cur.Elem1Ds[ei]) > 1.0) { idx[ei]++; any = true; }
                }
                if (!any) break;
                cur = RebuildSections(solved, idx, STEP);
                slv = new SolveLS(ref cur); cur = slv.Mdl;
            }
            return cur;
        }

        private static TB_Model RebuildSections(TB_Model tmpl, int[] idx, double step)
        {
            var els = new List<TB_Element_1D>();
            for (int i = 0; i < tmpl.Elem1Ds.Count; i++)
            {
                var o = tmpl.Elem1Ds[i];
                double d = (idx[i] + 1) * step;
                els.Add(new TB_Element_1D(o.Line, o.Tag,
                    new Section_Rect(o.Sec.Mat, string.Format("R{0}", d), d, d),
                    o.Vz, o.Buckling_Length));
            }
            return new TB_Model(els, tmpl.Sups, tmpl.Loads);
        }

        private static double ElemUtil(TB_Model m, TB_Element_1D el)
        {
            double nR = el.Sec.Area * el.Sec.Mat.Fy;
            double myR = el.Sec.Wy * el.Sec.Mat.Fy * 1e-3;
            double mzR = el.Sec.Wz * el.Sec.Mat.Fy * 1e-3;
            if (nR <= 0 || myR <= 0 || mzR <= 0) return double.MaxValue;
            double mx = 0;
            if (m.LCs == null) return 0;
            foreach (int lc in m.LCs)
            {
                int id = Array.IndexOf(m.LCs, lc);
                double[] F = el.Calc_Forces(id);
                double u = Math.Max(Math.Abs(F[0]), Math.Abs(F[6])) / nR
                         + Math.Max(Math.Abs(F[4]), Math.Abs(F[10])) / myR
                         + Math.Max(Math.Abs(F[5]), Math.Abs(F[11])) / mzR;
                if (u > mx) mx = u;
            }
            return mx;
        }

        private struct Entry
        {
            public SG_Shape Shape;
            public TB_Model Model;
            public IndividualRecord Rec;
            public int Gen, Ind;
        }

        #endregion

        protected override Bitmap Icon => Properties.Resources.icons_CAT_DataPreview;
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
            => new Guid("E5F6A7B8-9C0D-1E2F-3A4B-5C6D7E8F9A01");
    }

    #endregion
}
