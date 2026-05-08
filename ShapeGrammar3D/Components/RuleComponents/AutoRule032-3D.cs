using Grasshopper.Kernel;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule032_3D : GH_Component
    {
        public AutoRule032_3D()
            : base("Auto rule 032-3D", "A-Rule032-3D",
                  "GA rotation of struts/columns (Rule02: 3DAR2/AR2, -20/2). Optional plane: axis = plane Z. Leave plane disconnected for default: strut × init-curve tangent at foot.", UT.CAT, UT.GR_RLS)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "Optional: filter by exact element Name. Empty = all strut/column-like members (3DAR2, AR2, column, …). If your list matches no names, all candidates are used.", GH_ParamAccess.list);
            pManager.AddNumberParameter("AngleDomain", "AngleD", "[min, max] angle in radians (GA maps double gene into this range)", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Rotation Plane", "Pln", "Optional. If set, axis = plane ZAxis. If disconnected, per-strut strut × init-curve tangent.", GH_ParamAccess.item, Plane.WorldXY);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "Rule", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> eNames = new List<string>();
            List<double> domain = new List<double>();
            Plane rotationPlane = Plane.WorldXY;

            if (!DA.GetDataList(0, eNames)) return;
            if (!DA.GetDataList(1, domain)) return;

            // Important: an optional plane input can still expose a persistent default (WorldXY)
            // even when unwired. We only switch to global-axis mode when the input is actually wired.
            bool planeInputConnected = Params?.Input != null
                && Params.Input.Count > 2
                && Params.Input[2] != null
                && Params.Input[2].SourceCount > 0;

            SG_AutoRule032_3D rule;
            if (planeInputConnected && DA.GetData(2, ref rotationPlane))
                rule = new SG_AutoRule032_3D(eNames, domain.ToArray(), rotationPlane);
            else
                rule = new SG_AutoRule032_3D(eNames, domain.ToArray());

            DA.SetData(0, rule);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_Generic; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("449592DA-2B8E-4E21-9C0F-9F4D337B0E81"); }
        }
    }
}
