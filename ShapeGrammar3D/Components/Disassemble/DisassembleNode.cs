using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
    public class DisassembleNode : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DisassembleNode class.
        /// </summary>
        public DisassembleNode()
          : base("DisassembleNode", "Exp. Node",
              "",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Node", "SG_N", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Node id", "NID", "", GH_ParamAccess.item);
            pManager.AddGenericParameter("SG_Support", "SG_Sup", "", GH_ParamAccess.item);
            pManager.AddPointParameter("Point", "pt", "", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Plane", "pln", "", GH_ParamAccess.item);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            // --- variables ---
            SG_Node nd = new SG_Node();

            // --- input ---
            if (!DA.GetData(0, ref nd)) return;

            // --- solve ---
            int o_id = nd.ID;
            SG_Support sp = nd.Support;
            Point3d pt = nd.Pt;
            Plane pln = nd.NPln;

            // --- output ---
            DA.SetData(0, o_id);
            DA.SetData(1, sp);
            DA.SetData(2, pt);
            DA.SetData(3, pln);

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
            get { return new Guid("9f858f82-e087-4543-8775-c262d93b1d94"); }
        }
    }
}