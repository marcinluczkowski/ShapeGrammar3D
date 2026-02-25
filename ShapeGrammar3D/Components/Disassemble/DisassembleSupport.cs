using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
    public class DisassembleSupport : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DisassemleSupport class.
        /// </summary>
        public DisassembleSupport()
          : base("DisassemleSupport", "Exp. Sup",
              "",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Support", "SG_Sup", "SG_Support", GH_ParamAccess.item) ;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "", GH_ParamAccess.item);
            pManager.AddIntegerParameter("SupportCondition", "cond", "Integer representation of support condition", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ---variables-- -
            SG_Support sup = new SG_Support();

            // --- input ---
            if(!DA.GetData(0, ref sup)) return;

            // --- solve ---
            SG_Node n = sup.Node;
            int cond = sup.SupportCondition;

            // --- output ---
            DA.SetData(0, n);
            DA.SetData(1, cond);
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
            get { return new Guid("164ecb23-1d4b-4fd1-8fdc-8536a51eb685"); }
        }
    }
}