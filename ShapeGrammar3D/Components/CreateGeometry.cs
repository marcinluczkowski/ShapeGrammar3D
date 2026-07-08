using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class CreateGeometry : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CreateGeometry class.
        /// </summary>
        public CreateGeometry()
          : base("CreateGeometry", "geom",
              "Create geometry from Simple Shape",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Simple Shape", "sShape", "The Simple Shape element", GH_ParamAccess.item) ;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "l", "Lines from the simple shape", GH_ParamAccess.list);
            pManager.AddGenericParameter("Test", "test", "test", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            SG_Shape simpleShape = new SG_Shape();

            // --- input --- 
            if (!DA.GetData(0, ref simpleShape)) return;

            // --- solve ---
            List<Line> lines = simpleShape.GetLinesFromShape();
            List<int?> ids = new List<int?>();
            foreach (var item in simpleShape.Elems)
            {
                ids.Add(item.ID);
            }
            // --- output ---
            DA.SetDataList(0, lines);
            DA.SetDataList(1, ids);
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
            get { return new Guid("70361ee6-5a32-45c9-a4c7-8e7228a2c48d"); }
        }
    }
}