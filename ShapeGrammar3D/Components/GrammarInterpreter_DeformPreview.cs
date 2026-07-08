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

#pragma warning disable CS0618 // Archived component is intentionally referenced by its custom attributes.

namespace ShapeGrammar3D.Components
{
    #region Custom Attributes

    public class DeformPreviewAttributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds;
        private RectangleF _btnMesh;
        private RectangleF _btnUtilText;

        private const float BTN_H = 22f;
        private const float PAD = 4f;
        private const float MIN_W = 180f;

        private GrammarInterpreter_DeformPreview Comp
            => (GrammarInterpreter_DeformPreview)Owner;

        public DeformPreviewAttributes(GrammarInterpreter_DeformPreview owner)
            : base(owner) { }

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

            _btnMesh = new RectangleF(cx, y, cw, BTN_H);
            y += BTN_H + PAD;

            _btnUtilText = new RectangleF(cx, y, cw, BTN_H);
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

            DrawToggle(g, _btnMesh, "Utilization Mesh", Comp.ShowMesh);
            DrawToggle(g, _btnUtilText, "Utilization Text", Comp.ShowUtilText);
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
                if (_btnMesh.Contains(e.CanvasLocation))
                {
                    Owner.RecordUndoEvent("Toggle Utilization Mesh");
                    Comp.ShowMesh = !Comp.ShowMesh;
                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Handled;
                }
                if (_btnUtilText.Contains(e.CanvasLocation))
                {
                    Owner.RecordUndoEvent("Toggle Utilization Text");
                    Comp.ShowUtilText = !Comp.ShowUtilText;
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

    #region Component
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
    
    public class GrammarInterpreter_DeformPreview : GH_Component
    {
        public bool ShowMesh { get; set; }
        public bool ShowUtilText { get; set; }

        private struct UtilLabel
        {
            public Plane TextPlane;
            public string Text;
            public Color Colour;
        }
        private List<UtilLabel> _utilLabels = new List<UtilLabel>();
        private double _textHeight = 0.3;

        public GrammarInterpreter_DeformPreview()
          : base("GI_DeformPreview", "GI_DeformPreview",
              "Preview deformed structures from FEM results with utilization-coloured meshes",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override void CreateAttributes()
        {
            m_attributes = new DeformPreviewAttributes(this);
        }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!ShowUtilText || _utilLabels == null || _utilLabels.Count == 0) return;

            foreach (var lbl in _utilLabels)
                args.Display.Draw3dText(lbl.Text, lbl.Colour, lbl.TextPlane, _textHeight, "Arial");
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("ShowMesh", ShowMesh);
            writer.SetBoolean("ShowUtilText", ShowUtilText);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("ShowMesh")) ShowMesh = reader.GetBoolean("ShowMesh");
            if (reader.ItemExists("ShowUtilText")) ShowUtilText = reader.GetBoolean("ShowUtilText");
            return base.Read(reader);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models",
                "TB_Model data tree {generation}(individual) from Auto4", GH_ParamAccess.tree);       // 0
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display (-1 = all)", GH_ParamAccess.list);                     // 1
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices to display (-1 = all)", GH_ParamAccess.list);                     // 2
            pManager.AddNumberParameter("Scale", "Scale",
                "Deformation scale factor (1.0 = true scale)", GH_ParamAccess.item, 1.0);            // 3
            pManager.AddIntegerParameter("Load Case", "LC",
                "Load case index (-1 = last available)", GH_ParamAccess.item, -1);                    // 4
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);                    // 5
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between rows. Default (0, 0, -10).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingCompact);                // 6
            pManager.AddNumberParameter("Util Ranges", "URng",
                "5 utilization thresholds (%) defining 6 colour bands.\n" +
                "Default: 50, 80, 95, 100, 110\n" +
                "Band colours: Blue | Blue→Green | Green | Green→Yellow | Yellow→Red | Red",
                GH_ParamAccess.list);                                                                  // 7
            pManager.AddPointParameter("Insert Point", "InsPt",
                "Base point for the grid layout", GH_ParamAccess.item, Point3d.Origin);               // 8
            pManager.AddNumberParameter("Text Height", "TxH",
                "Text height in model units for utilization labels", GH_ParamAccess.item, 0.3);       // 9
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each cell's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);                                                          // 10

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[7].Optional = true;
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Undeformed", "Undef",
                "Undeformed element lines {col;row}(element)", GH_ParamAccess.tree);                  // 0
            pManager.AddLineParameter("Deformed", "Def",
                "Deformed element lines {col;row}(element)", GH_ParamAccess.tree);                    // 1
            pManager.AddNumberParameter("MaxDisp", "MaxD",
                "Maximum nodal displacement magnitude per individual {col;row}", GH_ParamAccess.tree);// 2
            pManager.AddTextParameter("Info", "Info",
                "Preview summary", GH_ParamAccess.item);                                              // 3
            pManager.AddMeshParameter("Meshes", "Meshes",
                "Utilization-coloured section meshes {col;row}(element)", GH_ParamAccess.tree);       // 4
            pManager.AddNumberParameter("Utilization", "Util",
                "Element utilization ratio {col;row}(element)", GH_ParamAccess.tree);                 // 5
            pManager.AddColourParameter("Colours", "Colours",
                "Utilization colour per element {col;row}(element)", GH_ParamAccess.tree);            // 6
            pManager.AddTextParameter("SecDims", "SecD",
                "Section dimensions per element (BxH mm) {col;row}(element)", GH_ParamAccess.tree);  // 7
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Structure<GH_TB_Model> modelsTree = new GH_Structure<GH_TB_Model>();
            if (!DA.GetDataTree(0, out modelsTree)) return;
            if (modelsTree == null || modelsTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No models provided.");
                return;
            }

            List<int> generationList = new List<int>();
            DA.GetDataList(1, generationList);

            List<int> individualList = new List<int>();
            DA.GetDataList(2, individualList);

            double scale = 1.0;
            int lcIndex = -1;
            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingCompact;
            Point3d insertPt = Point3d.Origin;

            DA.GetData(3, ref scale);
            DA.GetData(4, ref lcIndex);
            DA.GetData(5, ref colSpacing);
            DA.GetData(6, ref rowSpacing);

            var rawRanges = new List<double>();
            DA.GetDataList(7, rawRanges);
            double[] thresholds = ParseThresholds(rawRanges);

            DA.GetData(8, ref insertPt);

            double textH = 0.3;
            DA.GetData(9, ref textH);
            _textHeight = Math.Max(0.01, textH);

            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 10);

            _utilLabels.Clear();

            var genToBranch = new Dictionary<int, int>();
            for (int b = 0; b < modelsTree.PathCount; b++)
                genToBranch[modelsTree.Paths[b][0]] = b;

            if (generationList.Count == 0)
                generationList.Add(0);
            if (generationList.Contains(-1))
                generationList = genToBranch.Keys.OrderBy(k => k).ToList();

            bool allIndividuals = individualList.Count == 0 || individualList.Contains(-1);
            var indSet = new HashSet<int>(individualList);

            var undefTree = new GH_Structure<GH_Line>();
            var defTree = new GH_Structure<GH_Line>();
            var maxDispTree = new GH_Structure<GH_Number>();
            var meshTree = new GH_Structure<GH_Mesh>();
            var utilTree = new GH_Structure<GH_Number>();
            var colourTree = new GH_Structure<GH_Colour>();
            var secDimTree = new GH_Structure<GH_String>();

            int totalModels = 0;
            int maxRows = 0;

            for (int col = 0; col < generationList.Count; col++)
            {
                int genIdx = generationList[col];
                if (!genToBranch.TryGetValue(genIdx, out int branchIdx)) continue;

                GH_Path genPath = new GH_Path(genIdx);
                if (!modelsTree.PathExists(genPath)) continue;

                List<GH_TB_Model> modelBranch = (List<GH_TB_Model>)modelsTree.get_Branch(genPath);

                int row = 0;
                for (int i = 0; i < modelBranch.Count; i++)
                {
                    if (!allIndividuals && !indSet.Contains(i)) continue;
                    if (modelBranch[i] == null || modelBranch[i].Value == null) continue;

                    TB_Model model = modelBranch[i].Value;
                    if (model.Elem1Ds == null || model.Nodes == null) continue;

                    Point3d cellOrigin = insertPt + col * colSpacing + row * rowSpacing;
                    Vector3d offset = (Vector3d)cellOrigin;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform3D(displayPlane, cellOrigin);
                    GH_Path outPath = new GH_Path(col, row);

                    int resolvedLC = ResolveLoadCase(model, lcIndex);
                    bool hasDisps = resolvedLC >= 0;

                    double maxDisp = 0;

                    var nodeDefPts = new Dictionary<int, Point3d>();
                    if (hasDisps)
                    {
                        foreach (var node in model.Nodes)
                        {
                            if (node.Id == null) continue;
                            Point3d defPt = node.Pt;
                            if (node.Disps != null && resolvedLC < node.Disps.Count)
                            {
                                double[] d = node.Disps[resolvedLC];
                                Vector3d disp = new Vector3d(d[0] * scale, d[1] * scale, d[2] * scale);
                                defPt = node.Pt + disp;
                                double mag = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                                if (mag > maxDisp) maxDisp = mag;
                            }
                            nodeDefPts[node.Id.Value] = defPt;
                        }
                    }

                    for (int eIdx = 0; eIdx < model.Elem1Ds.Count; eIdx++)
                    {
                        var elem = model.Elem1Ds[eIdx];
                        if (elem.Nodes == null || elem.Nodes.Count < 2) continue;
                        var n0 = elem.Nodes[0];
                        var n1 = elem.Nodes[1];
                        if (n0 == null || n1 == null) continue;

                        Line undef = new Line(n0.Pt + offset, n1.Pt + offset);
                        undef.Transform(cellXf);
                        undefTree.Append(new GH_Line(undef), outPath);

                        Line defLine;
                        if (hasDisps && n0.Id != null && n1.Id != null
                            && nodeDefPts.ContainsKey(n0.Id.Value)
                            && nodeDefPts.ContainsKey(n1.Id.Value))
                        {
                            defLine = new Line(
                                nodeDefPts[n0.Id.Value] + offset,
                                nodeDefPts[n1.Id.Value] + offset);
                            defLine.Transform(cellXf);
                        }
                        else
                        {
                            defLine = undef;
                        }
                        defTree.Append(new GH_Line(defLine), outPath);

                        double util = ComputeUtilization(model, elem, resolvedLC);
                        utilTree.Append(new GH_Number(util), outPath);

                        Color clr = UtilizationColour(util, thresholds);
                        colourTree.Append(new GH_Colour(clr), outPath);

                        if (ShowUtilText)
                        {
                            Point3d midPt = defLine.PointAt(0.5);
                            Plane tpl = displayPlane.IsValid
                                ? new Plane(midPt, displayPlane.XAxis, displayPlane.YAxis)
                                : new Plane(midPt, Vector3d.XAxis, Vector3d.YAxis);
                            _utilLabels.Add(new UtilLabel
                            {
                                TextPlane = tpl,
                                Text = string.Format("{0:F1}%", util * 100.0),
                                Colour = clr
                            });
                        }

                        string secText = "N/A";
                        if (elem.Sec is Section_RHS rhs2)
                            secText = string.Format("SHS {0}x{1} t={2} mm", rhs2.W, rhs2.H, rhs2.Tw);
                        else if (elem.Sec is Section_Rect rect2)
                            secText = string.Format("{0}x{1} mm", rect2.B, rect2.H);
                        else if (elem.Sec != null)
                            secText = elem.Sec.GetDims();
                        secDimTree.Append(new GH_String(secText), outPath);

                        if (ShowMesh)
                        {
                            double sw = 0, sh = 0;
                            if (elem.Sec is Section_RHS rhs)
                            {
                                sw = rhs.W; sh = rhs.H;
                            }
                            else if (elem.Sec is Section_Rect rect)
                            {
                                sw = rect.B; sh = rect.H;
                            }

                            Color vc = Color.FromArgb(180, clr);
                            Mesh m = ExtrudeRect(defLine, sw * 0.001, sh * 0.001, vc);
                            if (m != null)
                                meshTree.Append(new GH_Mesh(m), outPath);
                        }
                    }

                    maxDispTree.Append(new GH_Number(maxDisp), outPath);
                    row++;
                    totalModels++;
                }

                if (row > maxRows) maxRows = row;
            }

            string rangeStr = string.Format("[0–{0}] [{0}–{1}] [{1}–{2}] [{2}–{3}] [{3}–{4}] [{4}+]",
                thresholds[0], thresholds[1], thresholds[2], thresholds[3], thresholds[4]);

            string info = string.Format(
                "Models: {0}\nGenerations: [{1}]\nScale: {2}\nLC: {3}\n" +
                "Layout: {4} cols x {5} rows\nMesh: {6}\nUtilText: {7}\n" +
                "Util ranges (%): {8}\n" +
                "Colours: Blue | Blue→Green | Green | Green→Yellow | Yellow→Red | Red",
                totalModels,
                string.Join(", ", generationList),
                scale,
                lcIndex == -1 ? "last" : lcIndex.ToString(),
                generationList.Count, maxRows,
                ShowMesh ? "ON" : "OFF",
                ShowUtilText ? "ON" : "OFF",
                rangeStr);

            DA.SetDataTree(0, undefTree);
            DA.SetDataTree(1, defTree);
            DA.SetDataTree(2, maxDispTree);
            DA.SetData(3, info);
            DA.SetDataTree(4, meshTree);
            DA.SetDataTree(5, utilTree);
            DA.SetDataTree(6, colourTree);
            DA.SetDataTree(7, secDimTree);
        }

        #region Helpers

        private static double[] ParseThresholds(List<double> raw)
        {
            double[] defaults = new double[] { 50, 80, 95, 100, 110 };
            if (raw == null || raw.Count < 5) return defaults;

            double[] t = new double[5];
            for (int i = 0; i < 5; i++)
                t[i] = raw[i];

            for (int i = 1; i < 5; i++)
                if (t[i] <= t[i - 1]) t[i] = t[i - 1] + 1;

            return t;
        }

        /// <summary>
        /// Maps utilization (%) to a colour using the 6-band scheme:
        ///   [0, t0]          → solid Blue
        ///   [t0, t1]         → Blue → Green
        ///   [t1, t2]         → solid Green
        ///   [t2, t3]         → Green → Yellow
        ///   [t3, t4]         → Yellow → Red
        ///   [t4, ∞)          → solid Red
        /// </summary>
        private static Color UtilizationColour(double utilRatio, double[] t)
        {
            double pct = utilRatio * 100.0;

            if (pct <= t[0])
                return Color.FromArgb(30, 100, 255);

            if (pct <= t[1])
            {
                double f = (pct - t[0]) / (t[1] - t[0]);
                return LerpColour(Color.FromArgb(30, 100, 255), Color.FromArgb(0, 200, 80), f);
            }

            if (pct <= t[2])
                return Color.FromArgb(0, 200, 80);

            if (pct <= t[3])
            {
                double f = (pct - t[2]) / (t[3] - t[2]);
                return LerpColour(Color.FromArgb(0, 200, 80), Color.FromArgb(255, 220, 0), f);
            }

            if (pct <= t[4])
            {
                double f = (pct - t[3]) / (t[4] - t[3]);
                return LerpColour(Color.FromArgb(255, 220, 0), Color.FromArgb(220, 30, 30), f);
            }

            return Color.FromArgb(220, 30, 30);
        }

        private static Color LerpColour(Color a, Color b, double f)
        {
            f = Math.Clamp(f, 0, 1);
            int r = (int)(a.R + (b.R - a.R) * f);
            int g = (int)(a.G + (b.G - a.G) * f);
            int bl = (int)(a.B + (b.B - a.B) * f);
            return Color.FromArgb(
                Math.Clamp(r, 0, 255),
                Math.Clamp(g, 0, 255),
                Math.Clamp(bl, 0, 255));
        }

        /// <summary>
        /// Simplified EC3 utilization: cross-section check (N/N_Rd + My/My_Rd + Mz/Mz_Rd)
        /// plus member buckling check (EC3 eq 6.61 / 6.62) for compression members.
        /// Returns utilization ratio (1.0 = 100%).
        /// </summary>
        private static double ComputeUtilization(TB_Model model, TB_Element_1D e, int lcIdx)
        {
            if (e.Sec == null || e.Sec.Mat == null) return 0;

            double gM0 = 1.0, gM1 = 1.0;

            double N_Rd = e.Sec.Area * e.Sec.Mat.Fy / gM0;
            double My_Rd = e.Sec.Wy * e.Sec.Mat.Fy / gM0 * 1e-3;
            double Mz_Rd = e.Sec.Wz * e.Sec.Mat.Fy / gM0 * 1e-3;

            if (N_Rd <= 0 || My_Rd <= 0 || Mz_Rd <= 0) return 0;

            double N_Rk = e.Sec.Area * e.Sec.Mat.Fy;
            double My_Rk = e.Sec.Wpy * e.Sec.Mat.Fy * 1e-3;
            double Mz_Rk = e.Sec.Wpz * e.Sec.Mat.Fy * 1e-3;

            double iy = Math.Sqrt(e.Sec.Iy / e.Sec.Area);
            double iz = Math.Sqrt(e.Sec.Iz / e.Sec.Area);

            double Lcr = e.Buckling_Length * 1000.0;
            double lambda1 = Math.PI * Math.Sqrt(e.Sec.Mat.E / e.Sec.Mat.Fy);
            double lby = iz > 0 && lambda1 > 0 ? (Lcr / iz) / lambda1 : 0;
            double lbz = iy > 0 && lambda1 > 0 ? (Lcr / iy) / lambda1 : 0;

            double alpha = e.Sec.Mat.Fy < 460 ? 0.21 : 0.13;
            double phiY = 0.5 * (1 + alpha * (lby - 0.2) + lby * lby);
            double phiZ = 0.5 * (1 + alpha * (lbz - 0.2) + lbz * lbz);

            double chiY = phiY > 0 && phiY * phiY >= lby * lby
                ? Math.Min(1.0 / (phiY + Math.Sqrt(phiY * phiY - lby * lby)), 1.0) : 1.0;
            double chiZ = phiZ > 0 && phiZ * phiZ >= lbz * lbz
                ? Math.Min(1.0 / (phiZ + Math.Sqrt(phiZ * phiZ - lbz * lbz)), 1.0) : 1.0;

            double Cmy = 0.9, Cmz = 0.9;
            double maxUtil = 0;

            if (model.LCs == null) return 0;

            foreach (int lc in model.LCs)
            {
                int id = Array.IndexOf(model.LCs, lc);
                double[] F = e.Calc_Forces(id);

                double N_Ed = Math.Max(Math.Abs(F[0]), Math.Abs(F[6]));
                double My_Ed = Math.Max(Math.Abs(F[4]), Math.Abs(F[10]));
                double Mz_Ed = Math.Max(Math.Abs(F[5]), Math.Abs(F[11]));

                double utilCS = N_Ed / N_Rd + My_Ed / My_Rd + Mz_Ed / Mz_Rd;

                double Nc = Math.Min(F[0], F[6]);
                if (Nc >= 0)
                {
                    if (utilCS > maxUtil) maxUtil = utilCS;
                    continue;
                }

                double chiYNRk = chiY * N_Rk / gM1;
                double chiZNRk = chiZ * N_Rk / gM1;
                if (chiYNRk <= 0 || chiZNRk <= 0) { if (utilCS > maxUtil) maxUtil = utilCS; continue; }

                double kyy = Math.Min(
                    Cmy * (1 + (lby - 0.2) * (N_Ed / chiYNRk)),
                    Cmy * (1 + 0.8 * (N_Ed / chiYNRk)));
                double kzz = Math.Min(
                    Cmz * (1 + (lbz - 0.2) * (N_Ed / chiZNRk)),
                    Cmz * (1 + 0.8 * (N_Ed / chiZNRk)));
                double kyz = 0.6 * kzz;
                double kzy = 0.6 * kyy;

                double myRk1 = My_Rk / gM1; if (myRk1 <= 0) myRk1 = 1;
                double mzRk1 = Mz_Rk / gM1; if (mzRk1 <= 0) mzRk1 = 1;

                double u661 = N_Ed / chiYNRk + kyy * My_Ed / myRk1 + kyz * Mz_Ed / mzRk1;
                double u662 = N_Ed / chiZNRk + kzy * My_Ed / myRk1 + kzz * Mz_Ed / mzRk1;

                double u = Math.Max(utilCS, Math.Max(u661, u662));
                if (u > maxUtil) maxUtil = u;
            }

            return maxUtil;
        }

        private static int ResolveLoadCase(TB_Model model, int requested)
        {
            if (model.Nodes == null || model.Nodes.Count == 0) return -1;
            var first = model.Nodes.FirstOrDefault(n => n.Disps != null && n.Disps.Count > 0);
            if (first == null) return -1;
            int num = first.Disps.Count;
            if (num == 0) return -1;
            return (requested < 0 || requested >= num) ? num - 1 : requested;
        }

        /// <summary>
        /// Builds a fully unwelded box mesh (24 vertices, 6 quad faces) so that
        /// every face has its own 4 vertices with independent vertex colours.
        /// This avoids the rendering artefact where shared-vertex colour
        /// interpolation leaves one corner of each quad looking uncoloured.
        /// </summary>
        private static Mesh ExtrudeRect(Line axis, double wM, double hM, Color faceColour)
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

            Point3d[] c = new Point3d[8];
            for (int end = 0; end < 2; end++)
            {
                Point3d o = end == 0 ? axis.From : axis.To;
                c[end * 4 + 0] = o - ly * hw - lz * hh;
                c[end * 4 + 1] = o + ly * hw - lz * hh;
                c[end * 4 + 2] = o + ly * hw + lz * hh;
                c[end * 4 + 3] = o - ly * hw + lz * hh;
            }

            int[][] faces = new int[][]
            {
                new[] { 0, 1, 5, 4 },
                new[] { 1, 2, 6, 5 },
                new[] { 2, 3, 7, 6 },
                new[] { 3, 0, 4, 7 },
                new[] { 0, 3, 2, 1 },
                new[] { 4, 5, 6, 7 },
            };

            var mesh = new Mesh();
            int vi = 0;
            foreach (var f in faces)
            {
                mesh.Vertices.Add(c[f[0]]);
                mesh.Vertices.Add(c[f[1]]);
                mesh.Vertices.Add(c[f[2]]);
                mesh.Vertices.Add(c[f[3]]);
                mesh.VertexColors.Add(faceColour);
                mesh.VertexColors.Add(faceColour);
                mesh.VertexColors.Add(faceColour);
                mesh.VertexColors.Add(faceColour);
                mesh.Faces.AddFace(vi, vi + 1, vi + 2, vi + 3);
                vi += 4;
            }

            mesh.Normals.ComputeNormals();
            return mesh;
        }

        #endregion

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_CAT_DataPreview;
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
            => new Guid("B2C3D4E5-6F7A-8B9C-0D1E-F2A3B4C5D6E7");
    }

    #endregion
}
