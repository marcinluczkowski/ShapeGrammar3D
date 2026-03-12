using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Select a standard Rectangular Hollow Section (RHS) from the catalog via right-click menus.
    /// Sizes from eurocodeapplied.com (hot-finished EN 10210-2). Outputs both SH and TB section objects.
    /// </summary>
    public class RHSPresetSection : GH_Component
    {
        private int _selectedH = 100;
        private int _selectedB = 60;
        private int _selectedT = 5;

        public RHSPresetSection()
          : base("RHS Preset", "RHS",
              "Select a Rectangular Hollow Section from the standard catalog (hot-finished RHS).\n" +
              "Right-click to choose Depth×Width and Thickness.",
              UT.CAT, UT.GR_SEC)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_Material(), "Material", "Mat",
                "TB_Material (use MaterialPreset or Material component)", GH_ParamAccess.item);
            pManager.AddGenericParameter("SH Material", "SH_Mat",
                "SH_Material for ShapeGrammar system (optional)", GH_ParamAccess.item);
            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.RegisterParam(new Param_Section(), "TB Section", "TB_Sec",
                "Section_RHS for Toolbox FEM", GH_ParamAccess.item);
            pManager.AddGenericParameter("SH Section", "SH_Sec",
                "SH_CrossSection_RHS for ShapeGrammar", GH_ParamAccess.item);
            pManager.AddCurveParameter("Curves", "Crvs",
                "Section outline curves", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info",
                "Section summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            ValidateSelection();

            GH_Material ghMat = null;
            DA.GetData(0, ref ghMat);
            SH_Material shMat = null;
            DA.GetData(1, ref shMat);

            TB_Material tbMat = ghMat?.Value;
            if (tbMat == null)
                tbMat = new TB_Material("S355", 210000, 80769, 78.5, 1.2e-5, 355);

            double h = _selectedH, b = _selectedB, t = _selectedT;
            string tag = string.Format("RHS_{0}x{1}x{2}", _selectedH, _selectedB, _selectedT);

            var sec = new Section_RHS(tbMat, tag, h, b, t, t);
            DA.SetData(0, new GH_Section(sec));
            DA.SetDataList(2, sec.Curves);

            if (shMat == null)
            {
                shMat = new SH_Material_Isotrop(
                    "Steel", tbMat.Tag,
                    tbMat.E, 0.3, tbMat.Fy,
                    tbMat.Gamma * 1000.0 / 9.81,
                    tbMat.Alpha);
            }

            var shSec = new SH_CrossSection_RHS(tag, h, b, t, t) { Material = shMat };
            DA.SetData(1, shSec);

            double area = sec.Area;
            double iy = sec.Iy;
            double wy = sec.Wy;
            string info = string.Format(
                "{0}\nA = {1:F1} mm²\nIy = {2:F0} mm⁴\nWy = {3:F1} mm³",
                tag, area, iy, wy);
            DA.SetData(3, info);

            Message = string.Format("{0}×{1}×{2}", _selectedH, _selectedB, _selectedT);
        }

        private void ValidateSelection()
        {
            var sizes = RHS_Catalog.AllSizes();
            if (sizes.All(s => s.H != _selectedH || s.B != _selectedB))
            {
                var first = sizes.FirstOrDefault();
                _selectedH = first.H;
                _selectedB = first.B;
            }
            var thicknesses = RHS_Catalog.ThicknessesFor(_selectedH, _selectedB);
            if (thicknesses != null && thicknesses.Length > 0 && !thicknesses.Contains(_selectedT))
                _selectedT = thicknesses[0];
        }

        #region Context menu

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            var sizeMenu = new ToolStripMenuItem("Select Size H×B (mm)");
            foreach (var (H, B) in RHS_Catalog.AllSizes())
            {
                int h = H, b = B;
                var item = new ToolStripMenuItem(
                    string.Format("{0} × {1}", h, b),
                    null,
                    (s, e) => { _selectedH = h; _selectedB = b; ValidateSelection(); ExpireSolution(true); })
                {
                    Checked = (h == _selectedH && b == _selectedB)
                };
                sizeMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(sizeMenu);

            var thickMenu = new ToolStripMenuItem("Select Thickness (mm)");
            foreach (int t in RHS_Catalog.ThicknessesFor(_selectedH, _selectedB))
            {
                int tt = t;
                var item = new ToolStripMenuItem(
                    string.Format("{0} mm", tt),
                    null,
                    (s, e) => { _selectedT = tt; ExpireSolution(true); })
                {
                    Checked = (tt == _selectedT)
                };
                thickMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(thickMenu);

            menu.Items.Add(new ToolStripSeparator());
            var allCombo = RHS_Catalog.AllCombinations();
            var quickMenu = new ToolStripMenuItem(string.Format("All RHS Sections ({0} total)", allCombo.Count));
            var grouped = allCombo.GroupBy(c => (c.H, c.B));
            foreach (var grp in grouped)
            {
                var subMenu = new ToolStripMenuItem(string.Format("{0}×{1}", grp.Key.H, grp.Key.B));
                foreach (var (H, B, T) in grp)
                {
                    int h = H, b = B, t = T;
                    var item = new ToolStripMenuItem(
                        string.Format("t = {0} mm", t),
                        null,
                        (s, e) =>
                        {
                            _selectedH = h;
                            _selectedB = b;
                            _selectedT = t;
                            ExpireSolution(true);
                        })
                    {
                        Checked = (h == _selectedH && b == _selectedB && t == _selectedT)
                    };
                    subMenu.DropDownItems.Add(item);
                }
                quickMenu.DropDownItems.Add(subMenu);
            }
            menu.Items.Add(quickMenu);
        }

        #endregion

        #region Serialization

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("RHS_H", _selectedH);
            writer.SetInt32("RHS_B", _selectedB);
            writer.SetInt32("RHS_T", _selectedT);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("RHS_H")) _selectedH = reader.GetInt32("RHS_H");
            if (reader.ItemExists("RHS_B")) _selectedB = reader.GetInt32("RHS_B");
            if (reader.ItemExists("RHS_T")) _selectedT = reader.GetInt32("RHS_T");
            return base.Read(reader);
        }

        #endregion

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D");
    }
}
