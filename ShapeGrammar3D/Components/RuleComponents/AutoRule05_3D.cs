using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class AutoRule05_3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AutoRule04 class.
        /// </summary>
        public AutoRule05_3D()
          : base("Auto Rule 05-3D", "A-Rule05-3D",
              "",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Rule option", "O", "options: 1 to the left, 2 to the right, 3 for both", GH_ParamAccess.list);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "Rule", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            string eName = "";
            // int option = -999;
            List<int> domain = new List<int>();

            // --- input ---
            if (!DA.GetData(0, ref eName)) return;
            // if (!DA.GetData(1, ref option)) return;
            if (!DA.GetDataList(1, domain)) return;

            // --- solve ---

            SG_AutoRule05_3D ar5 = new SG_AutoRule05_3D(eName, domain.ToArray());

            // --- output ---
            DA.SetData(0, ar5);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Properties.Resources.icons_Rule50;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;

        public override Guid ComponentGuid
        {
            get { return new Guid("E84034B3-15E0-4807-8466-E3E3FA9B93C2"); }
        }
    }
}