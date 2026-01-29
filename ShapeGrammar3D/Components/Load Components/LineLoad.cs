using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar.Components
{
    public class LineLoad : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the LineLoad class.
        /// </summary>
        public LineLoad()
          : base("LineLoad", "l_load",
              "Line load on element",
              UT.CAT, UT.GR_LD)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("ElementIds", "e_id", "The element to apply the loads onto", GH_ParamAccess.item); // 0
            pManager.AddIntegerParameter("LoadCase", "lc", "Load case that the load applies to", GH_ParamAccess.item, 0); // 1
            pManager.AddVectorParameter("LoadVector", "loadvec", "The direction of the line load", GH_ParamAccess.item); // 2

            pManager[0].Optional = true;  // element ids are not necessary. If none are present the laod applies to all. 
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_LineLoad", "load", "SH_Load for assembly", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            string elementID = "";
            int lc = 0;
            Vector3d lVec = new Vector3d();

            // --- input ---

            //bool idPresent = DA.GetData(0, ref elementID);
            DA.GetData(0,ref elementID);
            DA.GetData(1, ref lc);
            if (!DA.GetData(2, ref lVec)) return;
            // --- solve ---
            SG_LineLoad ll = new SG_LineLoad(lc, lVec)
            {
                ElementId = elementID
            };


            // --- output ---
            DA.SetData(0, ll);
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
                return ShapeGrammar3D.Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("d2f35a0d-76e0-4ebd-af17-8888febceab1"); }
        }
    }
}