using Grasshopper.Kernel;
using System;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class RHSCrossSection : GH_Component
    {
        public RHSCrossSection()
          : base("RHSCrossSection", "rhs_crossec",
              "Rectangular Hollow Section (pipe) cross-section",
              UT.CAT, UT.GR_SEC)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material", "mat", "Material.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width", "w", "Outer width B [mm]", GH_ParamAccess.item, 60.0);
            pManager.AddNumberParameter("Height", "h", "Outer height H [mm]", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Tw", "tw", "Web thickness [mm]", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Tf", "tf", "Flange thickness [mm]", GH_ParamAccess.item, 5.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Cross Section", "crossSec", "RHS cross section", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            SH_Material material = new SH_Material();
            double width = 60.0;
            double height = 100.0;
            double tw = 5.0;
            double tf = 5.0;

            if (!DA.GetData(0, ref material)) return;
            DA.GetData(1, ref width);
            DA.GetData(2, ref height);
            DA.GetData(3, ref tw);
            DA.GetData(4, ref tf);

            if (tw * 2 >= width || tf * 2 >= height)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Wall thickness must be less than half the outer dimension.");
                return;
            }

            string name = string.Format("RHS {0}x{1}x{2}x{3}", width, height, tw, tf);
            SH_CrossSection_RHS cs = new SH_CrossSection_RHS(name, height, width, tw, tf)
            {
                Material = material
            };

            DA.SetData(0, cs);
        }

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_C_Sec_I;
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
            => new Guid("D4E5F6A7-8B9C-0D1E-2F3A-B4C5D6E7F8A9");
    }
}
