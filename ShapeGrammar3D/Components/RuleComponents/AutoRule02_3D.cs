using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Components.RuleComponents
{
    public class AutoRule02_3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public AutoRule02_3D()
          : base("Auto rule 02-3D", "A-Rule02-3D",
              "Create struts along local Z from RULE010 beam tangents (avg → local X; Z×X→Y; X×Y→Z; if X∥Z use world +Z). Length from Domain × genotype.",
              UT.CAT, UT.GR_RLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Elem Name", "eName", "element name", GH_ParamAccess.list);
            pManager.AddNumberParameter("Domain", "D", "Length domain [min, max]", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Ratio", "minR",
                "Minimum ratio (0–1) of eligible nodes that receive struts. 0 = pure GA control, 0.5 = at least 50%.",
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
            List<double> domain = new List<double>();
            double minRatio = 0;

            if (!DA.GetDataList(0, eNames)) return;
            if (!DA.GetDataList(1, domain)) return;
            DA.GetData(2, ref minRatio);

            SG_AutoRule02_3D ar2 = new SG_AutoRule02_3D(eNames, domain.ToArray(), minRatio);

            if (minRatio > 0)
                AddRuntimeMessage(Grasshopper.Kernel.GH_RuntimeMessageLevel.Remark,
                    $"Min ratio: at least {minRatio:P0} of eligible nodes will receive struts");

            DA.SetData(0, ar2);
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
            get { return new Guid("c9a45760-3d3f-41a1-809c-60688049877c"); }
        }
    }
}