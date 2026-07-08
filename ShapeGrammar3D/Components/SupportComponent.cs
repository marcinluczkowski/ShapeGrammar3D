using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
namespace ShapeGrammar3D.Components
{
    public class SupportComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SupportComponent class.
        /// </summary>
        public SupportComponent()
          : base("SupportComponent", "support",
              "Create a support (SG_Support) with six DOF flags at a point, for use with Assembly.",
              UT.CAT, UT.GR_SUP)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Location", "loc",
                "Support position; must coincide with a node after Assembly merges endpoints.", GH_ParamAccess.item);
            pManager.AddTextParameter("DOF mask", "dof",
                "Six characters [Tx Ty Tz Rx Ry Rz]: 1 = fixed, 0 = free. Example: 111000 fixes translations only.", GH_ParamAccess.item, "111111");
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Support", "sup",
                "SG_Support instance (attach to Assembly supports list).", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Point3d location = new Point3d();
            string condition = "";

            // --- input --- 
            if (!DA.GetData(0, ref location)) return;
            DA.GetData(1, ref condition);

            // --- solve ---
            SG_Support support = new SG_Support(condition, location);

            // --- output ---
            DA.SetData(0, support);
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
                return Properties.Resources.icons_C_Sup;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("1a9f7c0e-6698-48ea-841a-5fff1f07f329"); }
        }
    }
}