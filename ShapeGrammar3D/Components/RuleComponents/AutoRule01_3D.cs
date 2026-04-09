using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule01_3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AutoRule01 class.
        /// </summary>
        public AutoRule01_3D()
          : base("Auto rule 01-3D", "A-Rule01-3D",
              "Split curves at a given parameter",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.list);
            pManager.AddNumberParameter("Max Segment Length", "maxSeg",
                "Maximum beam segment length. Beams longer than this are automatically subdivided into equal parts. Set 0 to use GA gene-based splitting.",
                GH_ParamAccess.item, 0);
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
            List<string> eNames = new List<string>();
            double maxSegLen = 0;

            if (!DA.GetDataList(0, eNames)) return;
            DA.GetData(1, ref maxSegLen);

            SG_AutoRule01_3D ar1 = new SG_AutoRule01_3D(eNames, maxSegLen);

            if (maxSegLen > 0)
                AddRuntimeMessage(Grasshopper.Kernel.GH_RuntimeMessageLevel.Remark,
                    $"Deterministic mode: beams will be split into segments ≤ {maxSegLen:F1}");

            DA.SetData(0, ar1);
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
            get { return new Guid("30E5B846-B02D-4577-8C3A-F93618984BEE"); }
        }
    }
}