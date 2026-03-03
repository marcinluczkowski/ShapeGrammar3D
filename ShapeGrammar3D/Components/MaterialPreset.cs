using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShapeGrammar3D.Components
{
    #region Preset definitions

    internal static class MaterialPresets
    {
        public struct MatDef
        {
            public string Family, Name;
            public double E, Poisson, Fy, Density, AlphaT;
            public double G_toolbox, Gamma_toolbox;
        }

        public static readonly MatDef S355 = new MatDef
        {
            Family = "Steel", Name = "S355",
            E = 210000, Poisson = 0.3, Fy = 355,
            Density = 7850, AlphaT = 1.2e-5,
            G_toolbox = 80769, Gamma_toolbox = 78.5
        };

        public static readonly MatDef S275 = new MatDef
        {
            Family = "Steel", Name = "S275",
            E = 210000, Poisson = 0.3, Fy = 275,
            Density = 7850, AlphaT = 1.2e-5,
            G_toolbox = 80769, Gamma_toolbox = 78.5
        };

        public static readonly MatDef C24 = new MatDef
        {
            Family = "Timber", Name = "C24",
            E = 11000, Poisson = 0.4, Fy = 24,
            Density = 420, AlphaT = 5e-6,
            G_toolbox = 690, Gamma_toolbox = 4.12
        };

        public static readonly MatDef C16 = new MatDef
        {
            Family = "Timber", Name = "C16",
            E = 8000, Poisson = 0.4, Fy = 16,
            Density = 370, AlphaT = 5e-6,
            G_toolbox = 500, Gamma_toolbox = 3.63
        };

        public static readonly MatDef[] All = { S355, S275, C24, C16 };
    }

    #endregion

    #region Custom Attributes

    public class MaterialPresetAttributes : GH_ComponentAttributes
    {
        private RectangleF _panelBounds;
        private readonly RectangleF[] _btns = new RectangleF[4];

        private const float BTN_H = 24f;
        private const float PAD = 4f;
        private const float GAP = 3f;
        private const float MIN_W = 180f;

        private static readonly string[] Labels = { "S355  (Steel)", "S275  (Steel)", "C24  (Timber)", "C16  (Timber)" };
        private static readonly Color[] ActiveColours =
        {
            Color.FromArgb(230, 41, 98, 255),
            Color.FromArgb(230, 41, 98, 255),
            Color.FromArgb(230, 139, 90, 43),
            Color.FromArgb(230, 139, 90, 43)
        };

        private MaterialPresetComponent Comp => (MaterialPresetComponent)Owner;

        public MaterialPresetAttributes(MaterialPresetComponent owner) : base(owner) { }

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

            for (int i = 0; i < 4; i++)
            {
                _btns[i] = new RectangleF(cx, y, cw, BTN_H);
                y += BTN_H + GAP;
            }

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

            for (int i = 0; i < 4; i++)
                DrawRadioButton(g, _btns[i], Labels[i], i == Comp.SelectedIndex, ActiveColours[i]);

            g.SmoothingMode = prev;
        }

        private static void DrawRadioButton(Graphics g, RectangleF r, string text, bool selected, Color activeClr)
        {
            Color bg = selected ? activeClr : Color.FromArgb(210, 215, 215, 215);
            Color border = selected ? Color.FromArgb(Math.Max(activeClr.R - 40, 0), Math.Max(activeClr.G - 40, 0), Math.Max(activeClr.B - 40, 0)) : Color.FromArgb(175, 175, 175);
            Color fg = selected ? Color.White : Color.FromArgb(70, 70, 70);

            using (var path = RoundRect(r, 4))
            {
                using (var fill = new SolidBrush(bg)) g.FillPath(fill, path);
                using (var pen = new Pen(border, 0.8f)) g.DrawPath(pen, path);
            }

            float rad = 7f;
            float cx = r.X + 8 + rad;
            float cy = r.Y + r.Height * 0.5f;
            using (var pen = new Pen(selected ? Color.White : Color.FromArgb(140, 140, 140), 1.5f))
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
            if (selected)
            {
                float ir = 4f;
                using (var fill = new SolidBrush(Color.White))
                    g.FillEllipse(fill, cx - ir, cy - ir, ir * 2, ir * 2);
            }

            RectangleF txt = new RectangleF(cx + rad + 6, r.Y, r.Width - rad * 2 - 22, r.Height);
            using (var brush = new SolidBrush(fg))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(text, GH_FontServer.Standard, brush, txt, sf);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (_btns[i].Contains(e.CanvasLocation))
                    {
                        Owner.RecordUndoEvent("Select material preset");
                        Comp.SelectedIndex = i;
                        Owner.ExpireSolution(true);
                        return GH_ObjectResponse.Handled;
                    }
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

    public class MaterialPresetComponent : GH_Component
    {
        public int SelectedIndex { get; set; }

        public MaterialPresetComponent()
          : base("MaterialPreset", "MatPreset",
              "Quick-select preset material (S355, S275, C24, C16)",
              UT.CAT, UT.GR_MAT)
        {
        }

        public override void CreateAttributes()
        {
            m_attributes = new MaterialPresetAttributes(this);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("SelectedIndex", SelectedIndex);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("SelectedIndex"))
                SelectedIndex = reader.GetInt32("SelectedIndex");
            return base.Read(reader);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH Material", "SH_Mat",
                "SH_Material_Isotrop for ShapeGrammar cross-sections", GH_ParamAccess.item);     // 0
            pManager.AddParameter(new Param_Material(), "TB Material", "TB_Mat",
                "TB_Material for Toolbox sections", GH_ParamAccess.item);                         // 1
            pManager.AddTextParameter("Info", "Info",
                "Selected material summary", GH_ParamAccess.item);                                // 2
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int idx = Math.Clamp(SelectedIndex, 0, MaterialPresets.All.Length - 1);
            var def = MaterialPresets.All[idx];

            var shMat = new SH_Material_Isotrop(
                def.Family, def.Name, def.E, def.Poisson, def.Fy, def.Density, def.AlphaT);

            var tbMat = new TB_Material(
                def.Name, def.E, def.G_toolbox, def.Gamma_toolbox, def.AlphaT, def.Fy);

            string info = string.Format(
                "{0} ({1})\nE={2} MPa  G={3} MPa\nFy={4} MPa  ρ={5} kg/m³\nγ={6} kN/m³  α={7}",
                def.Name, def.Family, def.E, def.G_toolbox,
                def.Fy, def.Density, def.Gamma_toolbox, def.AlphaT);

            DA.SetData(0, shMat);
            DA.SetData(1, new GH_Material(tbMat));
            DA.SetData(2, info);

            Message = def.Name;
        }

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("E5F6A7B8-9C0D-1E2F-3A4B-C5D6E7F8A9B0");
    }

    #endregion
}
