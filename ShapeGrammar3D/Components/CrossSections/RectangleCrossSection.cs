using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;


namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class RectangleCrossSection : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CrossSection class.
        /// </summary>
        public RectangleCrossSection()
          : base("RectangularCrossSection", "rec_crossec",
              "Define the CrossSection",
              UT.CAT, UT.GR_SEC)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material", "mat", "Material.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width", "w", "Width.", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Heigth", "h", "Heigth.", GH_ParamAccess.item, 50.0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Cross Section", "crossSec", "Rectangular cross section", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            double width = 0.0;
            double height = 0.0;
            SH_Material material = new SH_Material(); // For now, this only works for Isotrop material. How to make a constructor which work for both iso- and orthotropic materials

            // --- input ---
            if (!DA.GetData(0, ref material)) return;
            DA.GetData(1, ref width);
            DA.GetData(2, ref height);

            // --- solve ---
            SH_CrossSection_Rectangle crossSection = new SH_CrossSection_Rectangle("Test", height, width)
            {
                Material = material
            };


            // --- output ---
            DA.SetData(0, crossSection);
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
                return Properties.Resources.icons_C_Sec_I;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;

        public override Guid ComponentGuid
        {
            get { return new Guid("dca699fa-91c7-4400-8e78-ec1bbed3caa5"); }
        }
    }
}