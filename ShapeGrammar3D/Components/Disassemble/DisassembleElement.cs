using System;
using System.Collections.Generic;

using Rhino.Geometry;
using Grasshopper.Kernel;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Components
{
    public class DisassembleElement : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DisassembleElement class.
        /// </summary>
        public DisassembleElement()
          : base("DisassembleElement1D", "Exp. Elem",
              "",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Element1D", "SG_E", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("ID", "ID", "ID", GH_ParamAccess.item);
            pManager.AddTextParameter("Name", "Name", "Name", GH_ParamAccess.item);
            pManager.AddLineParameter("Line", "Ln", "Line", GH_ParamAccess.item);
            pManager.AddGenericParameter("SG_Nodes", "SG_Ns", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("SG_CroSec", "SG_S", "", GH_ParamAccess.item);
            pManager.AddPlaneParameter("EPln", "EPln", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            SG_Elem1D elem = new SG_Elem1D();

            // --- input ---
            if (!DA.GetData(0, ref elem)) return;

            // --- solve ---

            // var line = new Line(elem.EPln.Origin, elem.EPln.YAxis, elem.Ln.Length);

            // --- output ---
            DA.SetData(0, elem.ID);
            DA.SetData(1, elem.Name);
            DA.SetData(2, elem.Ln);
            // DA.SetData(2, line);
            DA.SetDataList(3, elem.Nodes);
            DA.SetData(4, elem.CrossSection);
            DA.SetData(5, elem.EPln);
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
                return null; // return Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("4cef31dc-480e-4276-898f-1e0341e78ef1"); }
        }
    }
}