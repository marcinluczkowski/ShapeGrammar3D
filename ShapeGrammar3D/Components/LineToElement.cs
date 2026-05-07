using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Components
{
    public class LineToElement : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Assembly class.
        /// </summary>
        public LineToElement()
          : base("LineToElement", "lnToEl",
              "Create a 1D element (SG_Elem1D) from a line, assigned cross-section, and optional label.",
              UT.CAT, UT.GR_ELM)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Initial Line", "initLine",
                "Member axis as a line; stored as Init_Crv and used in grammar derivation.", GH_ParamAccess.item); // 0
            pManager.AddGenericParameter("Cross Section", "crossSec",
                "Beam section (SH_CrossSection_Beam), e.g. rectangle or RHS preset.", GH_ParamAccess.item); // 1
            pManager.AddTextParameter("Element Name", "name",
                "Optional tag for the element (used to target line loads by name).", GH_ParamAccess.item); // 2

            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_Element", "sH_el",
                "The new SG_Elem1D instance to wire into Assembly.", GH_ParamAccess.item); 
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            // --- variables ---

            Line ln = new Line();
            SH_CrossSection_Beam crossSection = new SH_CrossSection_Beam();
            string name = "";

            // --- input --- 

            if (!DA.GetData(0, ref ln)) return;
            if (!DA.GetData(1, ref crossSection)) return;
            DA.GetData(2, ref name);

            // --- solve ---

            SG_Elem1D elem = new SG_Elem1D(ln, -999, name, crossSection);

            // --- output ---
            DA.SetData(0, elem);

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
            get { return new Guid("beaaf8ac-603a-49bf-9a4b-39ce573c5f44"); }
        }
    }
}