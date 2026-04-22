using Grasshopper.Kernel;
using Rhino.Geometry;
using System;

using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
    public class PointLoads : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the LoadComponent class.
        /// </summary>
        public PointLoads()
          : base("PointLoads", "loads",
              "Assign Loads To the structure",
              UT.CAT, UT.GR_LD)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddVectorParameter("Load Vector", "load", "Load as Vector in x,y,z direction", GH_ParamAccess.item, new Vector3d(0,0,0));
            pManager.AddVectorParameter("Moment Vector", "moment", "Moment as Vector", GH_ParamAccess.item, new Vector3d(0, 0, 0));
            pManager.AddPointParameter("Position", "pos", "Position as Point3d", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Loads", "loads", "An instance of the load class", GH_ParamAccess.item); 
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Vector3d forces = new Vector3d();
            Vector3d moments = new Vector3d();
            Point3d position = new Point3d();

            // --- input --- 
            if (!DA.GetData(2, ref position)) return;
            DA.GetData(0, ref forces);
            DA.GetData(1, ref moments);

            // --- solve ---
            SG_PointLoad load = new SG_PointLoad(forces, moments, position);



            // --- output ---
            DA.SetData(0, load);
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
            get { return new Guid("4e7d75ab-498c-4e30-9e3a-addf180b9a4d"); }
        }
    }
}