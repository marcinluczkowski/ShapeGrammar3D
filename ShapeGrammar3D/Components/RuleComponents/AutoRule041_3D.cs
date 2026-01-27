using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule041_3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AutoRule04 class.
        /// </summary>
        public AutoRule041_3D()
          : base("Auto Rule 041-3D", "A-Rule041-3D",
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
            int option = -999;
            List<int> domain = new List<int>();

            // --- input ---
            if (!DA.GetData(0, ref eName)) return;

            if (!DA.GetDataList(1, domain)) return;

            // --- solve ---

            SG_AutoRule041_3D ar41 = new SG_AutoRule041_3D(eName, domain.ToArray());

            // --- output ---
            DA.SetData(0, ar41);
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
                return Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("11F1D586-D34B-4084-BC33-CE7687146CA6"); }
        }
    }
}