using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;

using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
//    public class Rule03 : GH_Component
//    {
//        /// <summary>
//        /// Initializes a new instance of the Rule03 class.
//        /// </summary>
//        public Rule03()
//          : base("Rule03", "r3",
//              "Rule3 adds a funicular to the simple bridge. This is the last rule to be used",
//               UT.CAT, UT.GR_RLS)
//        {
//        }

//        /// <summary>
//        /// Registers all the input parameters for this component.
//        /// </summary>
//        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
//        {
//            pManager.AddNumberParameter("Horizontal Thrust", "thrust", "Horizontal force in the bridge as a factor of the vertical force", GH_ParamAccess.item, 0.7); // 0
//            pManager.AddBooleanParameter("Compression", "c", "True if the funicular shall be in compression; false for tension.", GH_ParamAccess.item, true); // 1
//        }

//        /// <summary>
//        /// Registers all the output parameters for this component.
//        /// </summary>
//        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
//        {
//            pManager.AddGenericParameter("SH_Rule03", "rule3", "", GH_ParamAccess.item); //
//        }

//        /// <summary>
//        /// This is the method that actually does the work.
//        /// </summary>
//        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
//        protected override void SolveInstance(IGH_DataAccess DA)
//        {
//            // --- variables ---
//            double h = 0.0;
//            bool c = true;

//            // --- input ---
//            DA.GetData(0, ref h);
//            DA.GetData(1, ref c);

//            // --- solve ---
//            SH_Rule03 rule3 = new SH_Rule03(h, c);

//            // --- output ---
//            DA.SetData(0, rule3);
//        }

//        /// <summary>
//        /// Provides an Icon for the component.
//        /// </summary>
//        protected override System.Drawing.Bitmap Icon
//        {
//            get
//            {
//                //You can add image files to your project resources and access them like this:
//                // return Resources.IconForThisComponent;
//                return Properties.Resources.icons_Generic;
//            }
//        }

//        /// <summary>
//        /// Gets the unique ID for this component. Do not change this ID after release.
//        /// </summary>
//        public override Guid ComponentGuid
//        {
//            get { return new Guid("e9cbf9fe-9c49-4e27-9fcd-071000a1ff3b"); }
//        }
//    }
}