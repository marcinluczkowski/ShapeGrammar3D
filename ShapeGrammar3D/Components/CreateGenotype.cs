using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class CreateGenotype : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CreateGenotype class.
        /// </summary>
        public CreateGenotype()
          : base("CreateGenotype", "Genotype",
              "Description",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Integer List", "IntLst", "", GH_ParamAccess.list);
            pManager.AddNumberParameter("Double List", "DblLst", "", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Genotype", "GType", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            List<int> ints = new List<int>();
            List<double> ds = new List<double>();

            // --- input ---
            if (!DA.GetDataList(0, ints)) return;
            if (!DA.GetDataList(1, ds)) return;

            // --- solve ---

            SG_Genotype gt = new SG_Genotype(ints, ds);

            // --- output ---
            DA.SetData(0, gt);

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
            get { return new Guid("89d40ccf-307e-4815-971a-8fff5ca2a2de"); }
        }
    }
}