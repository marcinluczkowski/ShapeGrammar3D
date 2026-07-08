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
    public class AutoRule031_3D : GH_Component
    {
        public AutoRule031_3D()
            : base("Auto rule 031-3D", "A-Rule031-3D",
                  "GA strut rotation: element name(s), angle domain. Axis = local Y from curve frame at strut base (like Rule 03-3D).", UT.CAT, UT.GR_RLS)
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "Element name(s); empty defaults to 3DAR2", GH_ParamAccess.list);
            pManager.AddNumberParameter("AngleDomain", "AngleD", "[min, max] angle in radians", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Rule", "Rule", "Rule", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> eNames = new List<string>();
            List<double> domain = new List<double>();

            if (!DA.GetDataList(0, eNames)) return;
            if (!DA.GetDataList(1, domain)) return;

            SG_AutoRule031_3D ar3_3d = new SG_AutoRule031_3D(eNames, domain.ToArray());

            DA.SetData(0, ar3_3d);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.icons_Rule31;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("328481C9-1A7A-46FD-B48D-E33C2263A29C"); }
        }

    }
}
