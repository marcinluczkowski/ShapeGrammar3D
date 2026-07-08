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
              "Create a 1D element (SG_Elem1D) from a NURBS/curve axis, cross-section, and optional name. Chord line connects curve endpoints; full curve kept for geometry and load snapping.",
              UT.CAT, UT.GR_ELM)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Initial Curve", "initCrv",
                "Member axis curve; duplicated as Init_Crv on the element.", GH_ParamAccess.item); // 0
            pManager.AddGenericParameter("Cross Section", "crossSec",
                "Beam section (SH_CrossSection_Beam).", GH_ParamAccess.item); // 1
            pManager.AddTextParameter("Element Name", "name",
                "Optional label (line loads can target this name).", GH_ParamAccess.item); // 2

            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_Element", "sH_el",
                "New SG_Elem1D for Assembly.", GH_ParamAccess.item);
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
                return Properties.Resources.icons_C_Elem1D;
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