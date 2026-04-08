using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule_InitShape_3D : GH_Component
    {
        public AutoRule_InitShape_3D()
          : base("Auto rule InitShape-3D", "A-InitShape",
              "GA places support points along boundary lines and creates " +
              "beams between points on different lines. " +
              "Required lines guarantee ≥ minPt supports; optional lines have no minimum.",
              UT.CAT, UT.GR_RLS)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Box", "box",
                "Design-space bounding box (kept for future domain checking)", GH_ParamAccess.item);
            pManager.AddLineParameter("Required Lines", "reqLn",
                "Boundary lines that must have ≥ minPt support points", GH_ParamAccess.list);
            pManager.AddLineParameter("Optional Lines", "optLn",
                "Additional boundary lines (no minimum point constraint)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Points Per Line", "maxPt",
                "Maximum candidate support points per boundary line", GH_ParamAccess.item, 4);
            pManager.AddIntegerParameter("Min Points", "minPt",
                "Minimum active points on each required line (≥2)", GH_ParamAccess.item, 3);
            pManager.AddGenericParameter("Cross Section", "crossSec",
                "Default cross section for generated beams", GH_ParamAccess.item);
            pManager.AddVectorParameter("Load", "load",
                "Load vector applied at every support node", GH_ParamAccess.item, new Vector3d(0, 0, -100));
            pManager.AddTextParameter("Support Condition", "supCond",
                "[Tx,Ty,Tz,Rx,Ry,Rz]", GH_ParamAccess.item, "111111");
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "InitShape rule instance", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Box box = Box.Unset;
            var reqLines = new List<Line>();
            var optLines = new List<Line>();
            int maxPt = 4;
            int minPt = 3;
            SH_CrossSection_Beam crossSec = null;
            var loadVec = new Vector3d();
            string supCond = "111111";

            if (!DA.GetData(0, ref box)) return;
            if (!DA.GetDataList(1, reqLines)) return;
            DA.GetDataList(2, optLines);
            DA.GetData(3, ref maxPt);
            DA.GetData(4, ref minPt);
            if (!DA.GetData(5, ref crossSec)) return;
            DA.GetData(6, ref loadVec);
            DA.GetData(7, ref supCond);

            if (reqLines.Count + optLines.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "At least 2 boundary lines (required + optional) are needed.");
                return;
            }

            var rule = new SG_AutoRule_InitShape_3D(
                box.BoundingBox, reqLines, optLines, maxPt, minPt,
                crossSec, loadVec, supCond);

            DA.SetData(0, rule);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901");
    }
}
