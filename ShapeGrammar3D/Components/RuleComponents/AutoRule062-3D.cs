using System;
using System.Collections.Generic;

using Grasshopper.Kernel;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule062_3D : GH_Component
    {
        public AutoRule062_3D()
          : base("Auto Rule 062-3D", "A-Rule062-3D",
              "Adds X-braces within the top-chord plane formed by rule061 ties. " +
              "For each base-beam pair connected by rule061, consecutive ties form " +
              "quadrilaterals, and rule062 inserts diagonals. Boundary tie uses a " +
              "virtual tie connecting the endpoints of the base beams. " +
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

            var ar = new SG_AutoRule062_3D(eName, domain.ToArray(), minRatio);
            DA.SetData(0, ar);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Rule62;

        public override Guid ComponentGuid => new Guid("1F1A9E86-5F10-4C1B-9A76-2B5B6A41C902");
    }
}
