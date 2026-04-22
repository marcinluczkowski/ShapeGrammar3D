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
              "Required lines guarantee ≥ minPt supports; optional lines have no minimum. " +
              "First two required lines get the same support count; minSpc enforces minimum spacing along each required line.",
              UT.CAT, UT.GR_RLS)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Boundary Brep", "bBrep",
                "Closed Brep design-space boundary.", GH_ParamAccess.item);
            pManager.AddMeshParameter("Boundary Mesh", "bMesh",
                "Closed Mesh design-space boundary. Used when Boundary Brep is not provided.", GH_ParamAccess.item);
            pManager.AddLineParameter("Required Lines", "reqLn",
                "Boundary lines that must have ≥ minPt support points", GH_ParamAccess.list);
            pManager.AddLineParameter("Optional Lines", "optLn",
                "Additional boundary lines (no minimum point constraint)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Points Per Line", "maxPt",
                "Maximum candidate support points per boundary line", GH_ParamAccess.item, 4);
            pManager.AddIntegerParameter("Min Points", "minPt",
                "Minimum active points on each required line (≥2)", GH_ParamAccess.item, 3);
            pManager.AddNumberParameter("Min Support Spacing", "minSpc",
                "Minimum chord distance between adjacent support points on each required line (model units). 0 = only merge coincident points.",
                GH_ParamAccess.item, 0.0);
            pManager.AddGenericParameter("Cross Section", "crossSec",
                "Default cross section for generated beams", GH_ParamAccess.item);
            pManager.AddVectorParameter("Load", "load",
                "Load vector applied at every support node", GH_ParamAccess.item, new Vector3d(0, 0, -100));
            pManager.AddTextParameter("Support Condition", "supCond",
                "[Tx,Ty,Tz,Rx,Ry,Rz]", GH_ParamAccess.item, "111111");
            pManager.AddVectorParameter("Area Load Vector", "areaLoad",
                "Surface load vector (kN/m2) on Box top face. Tributary area is computed by Voronoi and mapped to AR2 stud tips.",
                GH_ParamAccess.item, Vector3d.Zero);
            pManager.AddBooleanParameter("Use Self Weight", "selfW",
                "Apply self-weight in Grammar Interpreter from Boundary Shape (GI_FromBnd).", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Boundary Beam Constraint", "bConst",
                "0: no boundary beam constraint, 1: hard constraint (remove outside beams), >=2: soft feasibility weight.",
                GH_ParamAccess.item, 0);
            pManager[1].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "InitShape rule instance", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep boundaryBrep = null;
            Mesh boundaryMesh = null;
            var reqLines = new List<Line>();
            var optLines = new List<Line>();
            int maxPt = 4;
            int minPt = 3;
            double minSupportSpacing = 0.0;
            SH_CrossSection_Beam crossSec = null;
            var loadVec = new Vector3d();
            string supCond = "111111";
            var areaLoadVec = Vector3d.Zero;
            bool useSelfWeight = false;
            int boundaryBeamConstraint = 0;

            DA.GetData(0, ref boundaryBrep);
            DA.GetData(1, ref boundaryMesh);
            if (!DA.GetDataList(2, reqLines)) return;
            DA.GetDataList(3, optLines);
            DA.GetData(4, ref maxPt);
            DA.GetData(5, ref minPt);
            DA.GetData(6, ref minSupportSpacing);
            if (!DA.GetData(7, ref crossSec)) return;
            DA.GetData(8, ref loadVec);
            DA.GetData(9, ref supCond);
            DA.GetData(10, ref areaLoadVec);
            DA.GetData(11, ref useSelfWeight);
            DA.GetData(12, ref boundaryBeamConstraint);

            if (boundaryBrep == null && boundaryMesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Provide a closed Boundary Brep or Boundary Mesh.");
                return;
            }

            if (boundaryBrep != null && !boundaryBrep.IsSolid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary Brep must be closed (solid).");
                return;
            }

            if (boundaryBrep == null && boundaryMesh != null && !boundaryMesh.IsClosed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary Mesh must be closed.");
                return;
            }

            BoundingBox designBb;
            if (boundaryBrep != null)
                designBb = boundaryBrep.GetBoundingBox(true);
            else
                designBb = boundaryMesh.GetBoundingBox(true);

            if (reqLines.Count + optLines.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "At least 2 boundary lines (required + optional) are needed.");
                return;
            }

            var rule = new SG_AutoRule_InitShape_3D(
                designBb, reqLines, optLines, maxPt, minPt,
                crossSec, loadVec, supCond, areaLoadVec, useSelfWeight,
                boundaryBrep, boundaryMesh, boundaryBeamConstraint, minSupportSpacing);

            DA.SetData(0, rule);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901");
    }
}
