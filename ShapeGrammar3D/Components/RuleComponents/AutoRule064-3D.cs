using System;
using System.Collections.Generic;

using Grasshopper.Kernel;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule064_3D : GH_Component
    {
        public AutoRule064_3D()
          : base("Auto Rule 064-3D", "A-Rule064-3D",
              "Adds diagonals in the vertical plane between rule061 (top chord) and rule063 (bottom chord) ties that share the same InitShape base-beam pair. " +
              "Domain values: 1 = single diagonal, 2 = full X-brace.",
              UT.CAT, UT.GR_RLS)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Domain", "D", "Option domain [min, max]. 1 = single diagonal, 2 = full X-brace.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Ratio", "minR",
                "Minimum ratio (0–1) of eligible brace candidates that should be generated.",
                GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "Rule", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string eName = "";
            List<int> domain = new List<int>();
            double minRatio = 0.0;

            if (!DA.GetData(0, ref eName)) return;
            if (!DA.GetDataList(1, domain)) return;
            DA.GetData(2, ref minRatio);

            var ar = new SG_AutoRule064_3D(eName, domain.ToArray(), minRatio);
            DA.SetData(0, ar);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("3A8FD0C4-B2D9-4E1B-8FBB-1D4FBCE12E21");
    }
}
