using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Components.RuleComponents
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class AutoRule03_3D: GH_Component
    {
        public AutoRule03_3D()
            : base("Auto rule 03-3D", "A-Rule03-3D",
                  "Rotation", UT.CAT, UT.GR_RLS)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.list);
            // pManager.AddNumberParameter("Angle", "A", "", GH_ParamAccess.item);
            pManager.AddNumberParameter("AngleDomain", "AngleD", "", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "Rule", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            List<string> eNames = new List<string>();
            // double angle = 0.0;
            List<double> domain = new List<double>();

            // --- input ---
            if (!DA.GetDataList(0, eNames)) return;
            if (!DA.GetDataList(1, domain)) return;

            // --- solve ---
            SG_AutoRule030_3D ar3_3d = new SG_AutoRule030_3D(eNames, domain.ToArray());

            // --- output ---
            DA.SetData(0, ar3_3d);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return ShapeGrammar3D.Properties.Resources.icons_Rule30;
            }
        }
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
        {
            get { return new Guid("d7f2f057-1c9a-412d-ae93-d08ab1d966e3"); }
        }

    }
}
