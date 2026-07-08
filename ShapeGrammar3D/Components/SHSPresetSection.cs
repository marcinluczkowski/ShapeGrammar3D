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
    /// Select a standard Square Hollow Section from the catalog via right-click menus.
    /// Outputs both ShapeGrammar (SH) and Toolbox (TB) section objects.
    /// </summary>
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class SHSPresetSection : GH_Component
    {
        private int _selectedSize = 100;
        private int _selectedThickness = 4;

        public SHSPresetSection()
          : base("SHS Preset", "SHS",
              "Select a Square Hollow Section from the standard catalog.\n" +
              "Right-click to choose Size and Thickness.",
              UT.CAT, UT.GR_SEC)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_Material(), "Material", "Mat",
                "TB_Material (use MaterialPreset or Material component)", GH_ParamAccess.item);   // 0
            pManager.AddGenericParameter("SH Material", "SH_Mat",
                "SH_Material for ShapeGrammar system (optional)", GH_ParamAccess.item);           // 1

            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.RegisterParam(new Param_Section(), "TB Section", "TB_Sec",
                "Section_RHS for Toolbox FEM", GH_ParamAccess.item);                              // 0
            pManager.AddGenericParameter("SH Section", "SH_Sec",
                "SH_CrossSection_RHS for ShapeGrammar", GH_ParamAccess.item);                     // 1
            pManager.AddCurveParameter("Curves", "Crvs",
                "Section outline curves", GH_ParamAccess.list);                                    // 2
            pManager.AddTextParameter("Info", "Info",
                "Section summary", GH_ParamAccess.item);                                           // 3
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

            double s = _selectedSize;
            double t = _selectedThickness;
            string tag = string.Format("SHS {0}x{0}x{1}", _selectedSize, _selectedThickness);

            var sec = new Section_RHS(tbMat, tag, s, s, t, t);
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

            var shSec = new SH_CrossSection_RHS(tag, s, s, t, t) { Material = shMat };
            DA.SetData(1, shSec);

            double bi = s - 2 * t;
            double hi = bi;
            double area = s * s - bi * hi;
            double iy = (s * Math.Pow(s, 3) - bi * Math.Pow(hi, 3)) / 12.0;
            double wy = iy / (0.5 * s);

            string info = string.Format(
                "{0}\nA = {1:F1} mm²\nIy = {2:F0} mm⁴\nWy = {3:F1} mm³",
                tag, area, iy, wy);
            DA.SetData(3, info);

            Message = string.Format("{0}×{0}×{1}", _selectedSize, _selectedThickness);
        }

        private void ValidateSelection()
        {
            var entry = SHS_Catalog.FindBySize(_selectedSize);
            if (entry == null)
            {
                _selectedSize = 100;
                entry = SHS_Catalog.FindBySize(_selectedSize);
            }
            var e = entry.Value;
            _selectedThickness = Math.Clamp(_selectedThickness, e.Tmin, e.Tmax);
        }

        #region Context menu

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            var sizeMenu = new ToolStripMenuItem("Select Size (mm)");
            foreach (var entry in SHS_Catalog.Entries)
            {
                int sz = entry.Size;
                var item = new ToolStripMenuItem(
                    string.Format("{0} × {0}", sz),
                    null,
                    (s, e) => { _selectedSize = sz; ValidateSelection(); ExpireSolution(true); })
                {
                    Checked = (sz == _selectedSize)
                };
                sizeMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(sizeMenu);

            var thickMenu = new ToolStripMenuItem("Select Thickness (mm)");
            var curEntry = SHS_Catalog.FindBySize(_selectedSize);
            if (curEntry != null)
            {
                foreach (int t in curEntry.Value.ValidThicknesses())
                {
                    int tt = t;
                    var item = new ToolStripMenuItem(
                        string.Format("{0} mm", tt),
                        null,
                        (s, e) => { _selectedThickness = tt; ExpireSolution(true); })
                    {
                        Checked = (tt == _selectedThickness)
                    };
                    thickMenu.DropDownItems.Add(item);
                }
            }
            menu.Items.Add(thickMenu);

            menu.Items.Add(new ToolStripSeparator());

            var allCombo = SHS_Catalog.AllCombinations();
            var quickMenu = new ToolStripMenuItem(string.Format("All Sections ({0} total)", allCombo.Count));
            var grouped = allCombo.GroupBy(c => c.Size);
            foreach (var grp in grouped)
            {
                int sz = grp.Key;
                var subMenu = new ToolStripMenuItem(string.Format("{0}×{0}", sz));
                foreach (var (Size, T) in grp)
                {
                    int lsz = Size, lt = T;
                    var item = new ToolStripMenuItem(
                        string.Format("t = {0} mm", lt),
                        null,
                        (s, e) =>
                        {
                            _selectedSize = lsz;
                            _selectedThickness = lt;
                            ExpireSolution(true);
                        })
                    {
                        Checked = (lsz == _selectedSize && lt == _selectedThickness)
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
            writer.SetInt32("SHS_Size", _selectedSize);
            writer.SetInt32("SHS_Thickness", _selectedThickness);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("SHS_Size"))
                _selectedSize = reader.GetInt32("SHS_Size");
            if (reader.ItemExists("SHS_Thickness"))
                _selectedThickness = reader.GetInt32("SHS_Thickness");
            return base.Read(reader);
        }

        #endregion

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_C_Sec_I;
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
            => new Guid("F6A7B8C9-0D1E-2F3A-4B5C-D6E7F8A9B0C1");
    }
}
