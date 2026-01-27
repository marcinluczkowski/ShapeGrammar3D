using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Components
{
    public class DisassembleShape : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DisassembleSimpleShape class.
        /// </summary>
        public DisassembleShape()
          : base("DisassembleShape", "Exp. Shape",
              "",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_SimpleShape", "sShape", "The instance of a SH_SimpleShape to disassemble", GH_ParamAccess.item); // 0
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_Elements", "elems", "SH_Elements", GH_ParamAccess.list); // 0
            pManager.AddGenericParameter("SH_Supports", "sups", "SH_Supports", GH_ParamAccess.list); // 1
            pManager.AddGenericParameter("SH_Nodes", "nodes", "SH_Node", GH_ParamAccess.list); // 2

            // future implementations

            
            //pManager.AddGenericParameter("SH_Loads", "loads", "SH_Loads", GH_ParamAccess.list); // 0
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            SG_Shape ss = new SG_Shape();

            // --- input ---
            if (!DA.GetData(0, ref ss)) return;

            // --- solve ---

            // list of elements
            List<SG_Element> elems = new List<SG_Element>();
            elems.AddRange(ss.Elems);

            // list of supports
            List<SG_Support> sups = new List<SG_Support>();
            sups.AddRange(ss.Supports);

            // list of nodes
            List<SG_Node> nodes = new List<SG_Node>();
            nodes.AddRange(ss.Nodes);

            // --- output ---
            DA.SetDataList(0, elems);
            DA.SetDataList(1, sups);
            DA.SetDataList(2, nodes);



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
                return null; //return Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("86c43654-6fb9-4c5f-873f-7422e06a9d38"); }
        }
    }
}