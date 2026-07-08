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
              "Split beam/curve elements at gene-controlled positions. Optional Max Segment Length forces minimum division density while the GA gene varies node positions.",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "Element name filter. Only elements with matching names are subdivided.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Max Segment Length", "maxSeg",
                "Maximum beam segment length (model units). Beams longer than this are split into at least ceil(len/maxSeg) segments — this is the GA's lower bound on division count. " +
                "Set 0 (default) to use single gene-based splitting where the GA gene controls the split position on each element.",
                GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Min Segment Length", "minSeg",
                "Minimum allowed segment length (model units). Used together with Max Segment Length to define a *range* of allowed division counts: " +
                "for each beam the GA gene picks numSeg in [ceil(len/maxSeg), floor(len/minSeg)]. " +
                "Set 0 (default) to fix every individual at the minimum subdivision count (only phase varies).",
                GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Randomize Positions", "Rand",
                "When Max Segment Length > 0: if true (default), a GA gene per element shifts all interior nodes by a phase offset, " +
                "so nodes are not placed on a perfectly uniform grid. If false, interior nodes land at exact equal-step positions for the chosen division count.",
                GH_ParamAccess.item, true);
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
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
            double minSegLen = 0;
            bool randomize = true;

            if (!DA.GetDataList(0, eNames)) return;
            DA.GetData(1, ref maxSegLen);
            DA.GetData(2, ref minSegLen);
            DA.GetData(3, ref randomize);

            if (minSegLen > 0 && maxSegLen > 0 && minSegLen >= maxSegLen)
            {
                AddRuntimeMessage(Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning,
                    $"Min Segment Length ({minSegLen:F2}) must be smaller than Max Segment Length ({maxSegLen:F2}). Min ignored.");
                minSegLen = 0;
            }

            SG_AutoRule01_3D ar1 = new SG_AutoRule01_3D(eNames, maxSegLen, randomize, minSegLen);

            if (maxSegLen > 0)
            {
                string countMode = (minSegLen > 0 && minSegLen < maxSegLen)
                    ? $"count varies in [{minSegLen:F1}–{maxSegLen:F1}m] per beam"
                    : $"count fixed at minimum (≥{maxSegLen:F1}m segments)";
                string posMode = randomize ? "phase-shift positions" : "uniform positions";
                AddRuntimeMessage(Grasshopper.Kernel.GH_RuntimeMessageLevel.Remark,
                    $"MaxSeg mode: {countMode}, {posMode}");
            }

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
                return Properties.Resources.icons_Rule10;
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