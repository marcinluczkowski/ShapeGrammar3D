using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class DisassembleCroSec : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DisassembleCroSec class.
        /// </summary>
        public DisassembleCroSec()
          : base("Disassemble SG_CroSec", "Exp. CS",
              "",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Cross Section", "SG_CS", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---

            // --- input ---

            // --- solve ---

            // --- output ---
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
                return Properties.Resources.icons_CAT_Utilities;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;

        public override Guid ComponentGuid
        {
            get { return new Guid("653e34ed-1bfe-4edf-bef3-013b7f0f36e5"); }
        }
    }
}