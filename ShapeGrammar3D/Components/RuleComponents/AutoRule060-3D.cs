using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule060_3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AutoRule04 class.
        /// </summary>
        public AutoRule060_3D()
          : base("Auto Rule 060-3D", "A-Rule060-3D",
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
            // pManager.AddIntegerParameter("Rule option", "O", "options: 1 to the left, 2 to the right, 3 for both", GH_ParamAccess.list);

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
            List<int> domain = new List<int>();

            // --- input ---
            if (!DA.GetData(0, ref eName)) return;
            // if (!DA.GetDataList(1, domain)) return;

            // --- solve ---

            //SG_AutoRule05_3D ar5 = new SG_AutoRule05_3D(eName, domain.ToArray());
            SG_AutoRule060_3D ar60 = new SG_AutoRule060_3D(eName);

            // --- output ---
            DA.SetData(0, ar60);
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
            get { return new Guid("F57FF9BF-E6CB-478F-80FA-15D5290E66A5"); }
        }
    }
}