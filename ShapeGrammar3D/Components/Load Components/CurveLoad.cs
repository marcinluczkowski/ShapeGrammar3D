using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Curve / beam load: a uniform line load defined along an arbitrary curve.
    /// The Assembly component resolves it against the assembled elements,
    /// either as per-element line loads (LineToBeams) or as per-mid-node
    /// point loads sampled along the curve (PointsOnNodes).
    /// </summary>
    public class CurveLoad : GH_Component
    {
        public CurveLoad()
          : base("CurveLoad", "c_load",
              "Uniform line load along a curve. Resolved by the Assembly into either per-element line loads or per-mid-node point loads, depending on the distribution mode.",
              UT.CAT, UT.GR_LD)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "crv", "Curve along which the load is applied.", GH_ParamAccess.item);                                  // 0
            pManager.AddVectorParameter("Force per length", "f/L", "Uniform load vector in kN/m, in global XYZ.", GH_ParamAccess.item);                  // 1
            pManager.AddIntegerParameter("Distribution", "mode",
                "0 = LineToBeams (turn into SG_LineLoad on every overlapping element). 1 = PointsOnNodes (sample the curve and drop point loads on host beams as mid-nodes; element is NOT split).",
                GH_ParamAccess.item, 1);                                                                                                                // 2
            pManager.AddIntegerParameter("Subdivisions", "sub",
                "Number of stations to sample along the curve in PointsOnNodes mode. Ignored for LineToBeams.",
                GH_ParamAccess.item, 10);                                                                                                               // 3
            pManager.AddIntegerParameter("Load case", "lc", "Load case index.", GH_ParamAccess.item, 0);                                                 // 4

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_CurveLoad", "load", "An SG_CurveLoad ready to feed into the Assembly.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve crv = null;
            Vector3d fpl = Vector3d.Zero;
            int mode = 1;
            int sub = 10;
            int lc = 0;

            if (!DA.GetData(0, ref crv) || crv == null) return;
            if (!DA.GetData(1, ref fpl)) return;
            DA.GetData(2, ref mode);
            DA.GetData(3, ref sub);
            DA.GetData(4, ref lc);

            var nurbs = crv.ToNurbsCurve();
            if (nurbs == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not convert input curve to a NurbsCurve.");
                return;
            }

            CurveLoadDistribution dist = mode == 0
                ? CurveLoadDistribution.LineToBeams
                : CurveLoadDistribution.PointsOnNodes;

            var load = new SG_CurveLoad(nurbs, fpl, dist, sub, lc);

            DA.SetData(0, load);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_C_Load_P;

        public override Guid ComponentGuid => new Guid("3F4D8C12-2A5B-4E7E-91A0-7C0E3B58D2A1");
    }
}
