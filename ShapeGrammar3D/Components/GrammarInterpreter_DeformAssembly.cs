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
    #region DeformAssembly Attributes

    public class DeformAssemblyAttributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds, _btnMesh, _btnUtilText, _btnTop;
        private const float BTN_H = 22f, PAD = 4f, MIN_W = 180f;
        private GI_DeformAssembly Comp => (GI_DeformAssembly)Owner;

        public DeformAssemblyAttributes(GI_DeformAssembly owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            float w = Math.Max(Bounds.Width, MIN_W);
            float x = Bounds.X - (w - Bounds.Width) * 0.5f;
            float y = Bounds.Bottom + PAD * 2;
            _btnMesh = new RectangleF(x + PAD, y, w - PAD * 2, BTN_H);
            y += BTN_H + PAD;
            _btnUtilText = new RectangleF(x + PAD, y, w - PAD * 2, BTN_H);
            y += BTN_H + PAD;
            _btnTop = new RectangleF(x + PAD, y, w - PAD * 2, BTN_H);
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
            DrawToggle(g, _btnMesh, "Utilization Mesh", Comp.ShowMesh);
            DrawToggle(g, _btnUtilText, "Utilization Text", Comp.ShowUtilText);
            string topLbl = Comp.ShowTopPercent ? string.Format("Top {0}%", (Comp.CachedTopPercent * 100).ToString("F0")) : "Show Top %";
            DrawToggle(g, _btnTop, topLbl, Comp.ShowTopPercent);
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
                if (_btnMesh.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Utilization Mesh"); Comp.ShowMesh = !Comp.ShowMesh; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnUtilText.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Utilization Text"); Comp.ShowUtilText = !Comp.ShowUtilText; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
                if (_btnTop.Contains(e.CanvasLocation)) { Owner.RecordUndoEvent("Toggle Top %"); Comp.ShowTopPercent = !Comp.ShowTopPercent; Owner.ExpireSolution(true); return GH_ObjectResponse.Handled; }
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
    /// Deformation and utilization preview from SG Assembly. Inputs: Assembly, X spacing, Y spacing.
    /// </summary>
    public class GI_DeformAssembly : GH_Component
    {
        private struct UtilLabelA { public Point3d Position; public string Text; public Color Colour; }
        private List<UtilLabelA> _utilLabels = new List<UtilLabelA>();
        private double _textHeight = 0.3;

        public bool ShowMesh { get; set; } = true;
        public bool ShowUtilText { get; set; } = true;
        public bool ShowTopPercent { get; set; } = false;
        internal double CachedTopPercent { get; set; } = 0.05;

        public GI_DeformAssembly()
          : base("GI_Deform (Assembly)", "GI_Def_A",
              "Deformed structures and utilization from Assembly.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override void CreateAttributes() { m_attributes = new DeformAssemblyAttributes(this); }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("ShowMesh", ShowMesh);
            writer.SetBoolean("ShowUtilText", ShowUtilText);
            writer.SetBoolean("ShowTopPercent", ShowTopPercent);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("ShowMesh")) ShowMesh = reader.GetBoolean("ShowMesh");
            if (reader.ItemExists("ShowUtilText")) ShowUtilText = reader.GetBoolean("ShowUtilText");
            if (reader.ItemExists("ShowTopPercent")) ShowTopPercent = reader.GetBoolean("ShowTopPercent");
            return base.Read(reader);
        }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!ShowUtilText || _utilLabels == null || _utilLabels.Count == 0) return;
            foreach (var lbl in _utilLabels)
            {
                Plane pl = new Plane(lbl.Position, Vector3d.XAxis, Vector3d.YAxis);
                args.Display.Draw3dText(lbl.Text, lbl.Colour, pl, _textHeight, "Arial");
            }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_FromSg", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). Leave empty or -1 = all (default).", GH_ParamAccess.list,-1);
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual(s). Leave empty or -1 = all (default).", GH_ParamAccess.list,-1);
            pManager.AddNumberParameter("X Spacing", "dX", "Horizontal spacing", GH_ParamAccess.item, 30.0);
            pManager.AddNumberParameter("Y Spacing", "dY", "Vertical spacing", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Scale", "Scale", "Deformation scale factor (1.0 = true scale)", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Load Case", "LC", "Load case index (-1 = last available)", GH_ParamAccess.item, -1);
            pManager.AddNumberParameter("Util Ranges", "URng", "5 utilization thresholds (%) - default 50,80,95,100,110", GH_ParamAccess.list);
            pManager.AddPointParameter("Insert Point", "Pt", "Base point", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Text Height", "TxH", "Utilization label text height", GH_ParamAccess.item, 0.3);
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Show only top N best per cluster per generation. 0 = all (default). 1 = best one per cluster. Overrides Top % when > 0.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Top %", "Top%", "Top fraction per cluster when button ON and TopN=0 (0.0–1.0). 0.05 = 5%.", GH_ParamAccess.item, 0.05);
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Undeformed", "Undef", "Undeformed lines", GH_ParamAccess.tree);
            pManager.AddLineParameter("Deformed", "Def", "Deformed lines", GH_ParamAccess.tree);
            pManager.AddNumberParameter("MaxDisp", "MaxD", "Maximum displacement per model", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
            pManager.AddMeshParameter("Meshes", "Meshes", "Utilization-coloured meshes", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Utilization", "Util", "Element utilization", GH_ParamAccess.tree);
            pManager.AddColourParameter("Colours", "Colours", "Utilization colours", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _utilLabels.Clear();

            GH_SGAssembly ghAssembly = null;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly required.");
                return;
            }
            var assembly = ghAssembly.Value;

            List<int> genList = new List<int>();
            List<int> indList = new List<int>();
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            double xSpacing = 30.0, ySpacing = 10.0, scale = 1.0;
            int lcIndex = -1;
            Point3d insertPt = Point3d.Origin;
            var rawRanges = new List<double>();
            DA.GetData(3, ref xSpacing);
            DA.GetData(4, ref ySpacing);
            DA.GetData(5, ref scale);
            DA.GetData(6, ref lcIndex);
            DA.GetDataList(7, rawRanges);
            DA.GetData(8, ref insertPt);
            double textH = 0.3, topPercent = 0.05;
            int topN = 0;
            DA.GetData(9, ref textH);
            DA.GetData(10, ref topN);
            DA.GetData(11, ref topPercent);
            _textHeight = Math.Max(0.01, textH);
            topPercent = Math.Clamp(topPercent, 0.001, 1.0);
            CachedTopPercent = topPercent;
            topN = Math.Max(0, topN);

            double[] thresholds = ParseThresholds(rawRanges);

            // Build elite set: top N per cluster or top N% when ShowTopPercent and topN=0
            var eliteSet = new HashSet<(int gen, int ind)>();
            if (topN > 0 || ShowTopPercent)
            {
                var candidates = new List<(int gen, int ind, double fit, int clust)>();
                foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
                {
                    if (!allGens && !genList.Contains(gen.Generation)) continue;
                    for (int indIdx = 0; indIdx < (gen.Individuals?.Count ?? 0); indIdx++)
                    {
                        if (!allInds && indSet != null && !indSet.Contains(indIdx)) continue;
                        var ind = gen.Individuals[indIdx];
                        if (ind == null || ind.Fitness >= double.MaxValue * 0.5) continue;
                        candidates.Add((gen.Generation, indIdx, ind.Fitness, ind.ClustGrp));
                    }
                }
                foreach (var grp in candidates.GroupBy(c => (c.gen, c.clust)))
                {
                    var ordered = grp.OrderBy(c => c.fit).ToList();
                    int keep = topN > 0 ? Math.Min(topN, ordered.Count) : Math.Max(1, (int)Math.Ceiling(ordered.Count * topPercent));
                    for (int k = 0; k < keep && k < ordered.Count; k++)
                        eliteSet.Add((ordered[k].gen, ordered[k].ind));
                }
            }

            var undefTree = new GH_Structure<GH_Line>();
            var defTree = new GH_Structure<GH_Line>();
            var maxDispTree = new GH_Structure<GH_Number>();
            var meshTree = new GH_Structure<GH_Mesh>();
            var utilTree = new GH_Structure<GH_Number>();
            var colourTree = new GH_Structure<GH_Colour>();

            int col = 0;
            int totalModels = 0;
            foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
            {
                if (!allGens && !genList.Contains(gen.Generation)) continue;
                int row = 0;
                for (int indIdx = 0; indIdx < (gen.Individuals?.Count ?? 0); indIdx++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(indIdx))) continue;
                    if ((topN > 0 || ShowTopPercent) && !eliteSet.Contains((gen.Generation, indIdx))) continue;
                    var ind = gen.Individuals[indIdx];
                    TB_Model model = ind?.Model;
                    if (model == null || model.Elem1Ds == null || model.Nodes == null) { row++; continue; }

                    Vector3d offset = new Vector3d(
                        insertPt.X + col * xSpacing,
                        insertPt.Y - row * ySpacing,
                        insertPt.Z);
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

                    foreach (var elem in model.Elem1Ds)
                    {
                        if (elem.Nodes == null || elem.Nodes.Count < 2) continue;
                        var n0 = elem.Nodes[0];
                        var n1 = elem.Nodes[1];
                        if (n0 == null || n1 == null) continue;

                        Line undef = new Line(n0.Pt + offset, n1.Pt + offset);
                        undefTree.Append(new GH_Line(undef), outPath);

                        Line defLine;
                        if (hasDisps && n0.Id != null && n1.Id != null
                            && nodeDefPts.ContainsKey(n0.Id.Value) && nodeDefPts.ContainsKey(n1.Id.Value))
                        {
                            defLine = new Line(
                                nodeDefPts[n0.Id.Value] + offset,
                                nodeDefPts[n1.Id.Value] + offset);
                        }
                        else defLine = undef;
                        defTree.Append(new GH_Line(defLine), outPath);

                        double util = ComputeUtilization(model, elem, resolvedLC);
                        utilTree.Append(new GH_Number(util), outPath);
                        Color clr = UtilizationColour(util, thresholds);
                        colourTree.Append(new GH_Colour(clr), outPath);

                        if (ShowUtilText)
                        {
                            Point3d midPt = defLine.PointAt(0.5);
                            _utilLabels.Add(new UtilLabelA { Position = midPt, Text = string.Format("{0:F1}%", util * 100.0), Colour = clr });
                        }

                        if (ShowMesh)
                        {
                            double sw = 0, sh = 0;
                            if (elem.Sec is Section_RHS rhs) { sw = rhs.W; sh = rhs.H; }
                            else if (elem.Sec is Section_Rect rect) { sw = rect.B; sh = rect.H; }
                            if (sw > 0 && sh > 0)
                            {
                                Mesh m = ExtrudeRect(defLine, sw * 0.001, sh * 0.001, Color.FromArgb(180, clr));
                                if (m != null) meshTree.Append(new GH_Mesh(m), outPath);
                            }
                        }
                    }

                    maxDispTree.Append(new GH_Number(maxDisp), outPath);
                    totalModels++;
                    row++;
                }
                col++;
            }

            string rangeStr = string.Format("[0–{0}] [{0}–{1}] [{1}–{2}] [{2}–{3}] [{3}–{4}] [{4}+]",
                thresholds[0], thresholds[1], thresholds[2], thresholds[3], thresholds[4]);
            DA.SetDataTree(0, undefTree);
            DA.SetDataTree(1, defTree);
            DA.SetDataTree(2, maxDispTree);
            DA.SetData(3, string.Format("Deform from Assembly: {0} models. Mesh: {1}, UtilText: {2}. Ranges: {3}",
                totalModels, ShowMesh ? "ON" : "OFF", ShowUtilText ? "ON" : "OFF", rangeStr));
            DA.SetDataTree(4, meshTree);
            DA.SetDataTree(5, utilTree);
            DA.SetDataTree(6, colourTree);
        }

        #region Helpers

        private static double[] ParseThresholds(List<double> raw)
        {
            double[] defaults = new double[] { 50, 80, 95, 100, 110 };
            if (raw == null || raw.Count < 5) return defaults;
            double[] t = new double[5];
            for (int i = 0; i < 5; i++) t[i] = raw[i];
            for (int i = 1; i < 5; i++)
                if (t[i] <= t[i - 1]) t[i] = t[i - 1] + 1;
            return t;
        }

        private static Color UtilizationColour(double utilRatio, double[] t)
        {
            double pct = utilRatio * 100.0;
            if (pct <= t[0]) return Color.FromArgb(30, 100, 255);
            if (pct <= t[1])
            {
                double f = (pct - t[0]) / (t[1] - t[0]);
                return LerpColour(Color.FromArgb(30, 100, 255), Color.FromArgb(0, 200, 80), f);
            }
            if (pct <= t[2]) return Color.FromArgb(0, 200, 80);
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
            return Color.FromArgb(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(bl, 0, 255));
        }

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
            double chiY = phiY > 0 && phiY * phiY >= lby * lby ? Math.Min(1.0 / (phiY + Math.Sqrt(phiY * phiY - lby * lby)), 1.0) : 1.0;
            double chiZ = phiZ > 0 && phiZ * phiZ >= lbz * lbz ? Math.Min(1.0 / (phiZ + Math.Sqrt(phiZ * phiZ - lbz * lbz)), 1.0) : 1.0;
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
                if (Nc >= 0) { if (utilCS > maxUtil) maxUtil = utilCS; continue; }
                double chiYNRk = chiY * N_Rk / gM1;
                double chiZNRk = chiZ * N_Rk / gM1;
                if (chiYNRk <= 0 || chiZNRk <= 0) { if (utilCS > maxUtil) maxUtil = utilCS; continue; }
                double kyy = Math.Min(Cmy * (1 + (lby - 0.2) * (N_Ed / chiYNRk)), Cmy * (1 + 0.8 * (N_Ed / chiYNRk)));
                double kzz = Math.Min(Cmz * (1 + (lbz - 0.2) * (N_Ed / chiZNRk)), Cmz * (1 + 0.8 * (N_Ed / chiZNRk)));
                double kyz = 0.6 * kzz; double kzy = 0.6 * kyy;
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
            Point3d[] c = new Point3d[8];
            for (int end = 0; end < 2; end++)
            {
                Point3d o = end == 0 ? axis.From : axis.To;
                c[end * 4 + 0] = o - ly * hw - lz * hh;
                c[end * 4 + 1] = o + ly * hw - lz * hh;
                c[end * 4 + 2] = o + ly * hw + lz * hh;
                c[end * 4 + 3] = o - ly * hw + lz * hh;
            }
            int[][] faces = new int[][] {
                new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 },
                new[] { 3, 0, 4, 7 }, new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 } };
            var mesh = new Mesh();
            int vi = 0;
            foreach (var f in faces)
            {
                mesh.Vertices.Add(c[f[0]]); mesh.Vertices.Add(c[f[1]]); mesh.Vertices.Add(c[f[2]]); mesh.Vertices.Add(c[f[3]]);
                mesh.VertexColors.Add(faceColour); mesh.VertexColors.Add(faceColour); mesh.VertexColors.Add(faceColour); mesh.VertexColors.Add(faceColour);
                mesh.Faces.AddFace(vi, vi + 1, vi + 2, vi + 3);
                vi += 4;
            }
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        #endregion

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("C3D4E5F6-A7B8-9012-CDEF-012345678902");
    }
}
