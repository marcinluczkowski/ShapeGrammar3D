using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;

using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components
{
    public class Rule01 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Rule01 class.
        /// </summary>
        public Rule01()
          : base("Rule01", "Nickname",
              "Description",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddVectorParameter("Vector1", "v1", "Translation vector to move endpoint of line", GH_ParamAccess.item);
            pManager.AddVectorParameter("Vector2", "v2", "Translation vector to move endpoint of line", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule Class 1", "rule1", "The Rule to be applied in the Shape Grammar", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Vector3d vec1 = new Vector3d();
            Vector3d vec2 = new Vector3d();
            // --- input --- 
            if (!DA.GetData(0, ref vec1)) return;
            if (!DA.GetData(1, ref vec2)) return;

            // --- solve ---
            SH_Rule01 rule1 = new SH_Rule01(vec1, vec2);




            // --- output ---
            DA.SetData(0, rule1);
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
            get { return new Guid("cb37daa8-6fd5-4ba6-a22b-51c9fa81bf32"); }
        }
    }
}