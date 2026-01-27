using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Components
{
    public class CrvToElement : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Assembly class.
        /// </summary>
        public CrvToElement()
          : base("CurveToElement", "crvToEl",
              "Creates a SH_Element from a Curve",
              UT.CAT, UT.GR_ELM)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Initial Crv", "initCrv", "Curve to be used in the simple grammar derivaiton.", GH_ParamAccess.item); // 0
            pManager.AddGenericParameter("Cross Section", "crossSec", "Cross Section to assign the element", GH_ParamAccess.item); // 1
            pManager.AddTextParameter("ElementName", "name", "Name of the element", GH_ParamAccess.item); // 2

            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_Element", "sH_el", "An instance of a SH_Element", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            // --- variables ---

            //Line ln = new Line();
            Curve crv = new Line().ToNurbsCurve(); 
            SH_CrossSection_Beam crossSection = new SH_CrossSection_Beam();
            string name = "";

            // --- input --- 

            if (!DA.GetData(0, ref crv)) return;
            if (!DA.GetData(1, ref crossSection)) return;
            DA.GetData(2, ref name);

            // --- solve ---

            SG_Elem1D elem = new SG_Elem1D(crv, -999, name, crossSection);

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
                return null; // Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("80E3C2B9-0871-488A-BBFE-73532A64D6A6"); }
        }
    }
}