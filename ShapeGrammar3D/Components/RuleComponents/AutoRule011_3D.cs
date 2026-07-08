using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule011_3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AutoRule01 class.
        /// </summary>
        public AutoRule011_3D()
          : base("Auto rule 011-3D", "A-Rule011-3D",
              "Determines number of studs at each node",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.list);
            pManager.AddNumberParameter("Domain", "Domain", "", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "Rule", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            List<string> eNames = new List<string>();
            List<double> domain = new List<double>();

            // --- input ---
            if (!DA.GetDataList(0, eNames)) return;
            if (!DA.GetDataList(1, domain)) return;


            // --- solve ---

            SG_AutoRule011_3D ar11 = new SG_AutoRule011_3D(eNames, domain.ToArray());

            // --- output ---
            DA.SetData(0, ar11);

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
                return Properties.Resources.icons_Rule11;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("9080ECB7-9C2B-4A34-9E8D-5EA714AB4873"); }
        }
    }
}