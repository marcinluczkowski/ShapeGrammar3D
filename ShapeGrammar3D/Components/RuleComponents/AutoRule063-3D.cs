using System;
using System.Collections.Generic;

using Grasshopper.Kernel;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule063_3D : GH_Component
    {
        public AutoRule063_3D()
          : base("Auto Rule 063-3D", "A-Rule063-3D",
              "Bottom-chord connector. For each RULE020 strut, only the two adjacent InitShape base beams are considered. " +
              "On each adjacent beam, the nearest strut foot is picked and a brace is added to the focus strut foot. " +
              "Domain [min, max] encodes the option: 1 = left, 2 = right, 3 = both.",
              UT.CAT, UT.GR_RLS)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Domain", "D", "Option domain [min, max]. 1 = left, 2 = right, 3 = both.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Ratio", "minR",
                "Minimum ratio (0–1) of eligible struts that should generate members.",
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

            var ar = new SG_AutoRule063_3D(eName, domain.ToArray(), minRatio);
            DA.SetData(0, ar);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("2F6E3D2A-7C22-4B0D-9D35-B78F6C3D51E4");
    }
}
