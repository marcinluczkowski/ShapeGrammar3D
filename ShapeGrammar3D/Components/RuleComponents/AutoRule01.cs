using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class AutoRule01 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AutoRule01 class.
        /// </summary>
        public AutoRule01()
          : base("Auto rule 01", "A-Rule01",
              "Split curves at a given parameter",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.list);
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
            List<string> eNames = new List<string>();

            // --- input ---
            if(!DA.GetDataList(0, eNames)) return;

            // --- solve ---

            SG_AutoRule01 ar1 = new SG_AutoRule01(eNames);

            // --- output ---
            DA.SetData(0, ar1);

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
                return ShapeGrammar3D.Properties.Resources.icons_Rule10;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;

        public override Guid ComponentGuid
        {
            get { return new Guid("f3e48098-635b-49f3-b840-f490a8ac83eb"); }
        }
    }
}