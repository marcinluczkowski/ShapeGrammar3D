using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components
{
    public class Rule02 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Rule02 class.
        /// </summary>
        public Rule02()
          : base("Rule02", "rule2",
              "Rule 2 which splits a line at its given parameter",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("ElementID", "ID", "The ID of the element to operate on", GH_ParamAccess.item);
            pManager.AddNumberParameter("Split parameter", "splitParam", "The parameter to split the line", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule Class 2", "rule2", "The Rule to be applied in the Shape Grammar", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            int elId = 0;
            double t = 0.0;

            // --- input --- 
            if (!DA.GetData(0, ref elId)) return;
            if (!DA.GetData(1, ref t)) return;

            // --- solve ---
            SH_Rule02 rule2 = new SH_Rule02(elId, t);

            // --- output ---
            DA.SetData(0, rule2);
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
            get { return new Guid("5a267320-425c-4502-8516-25b8cb828d0b"); }
        }
    }
}