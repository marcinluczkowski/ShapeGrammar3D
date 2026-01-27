using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using ShapeGrammar3D.Components.ToolboxParameters;

namespace ShapeGrammar3D
{

    public class SolveLS4SG : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the _07_AnalyseLS class.
        /// </summary>
        public SolveLS4SG()
          : base("Solve Linear Static for Shape Grammar", "Solve LS4SG",
              "Solve Linear Static based on the SG assembly component",
              Common.category, Common.sub_analize)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //pManager.AddParameter(new Param_Model(), "Model", "Model", "Model", GH_ParamAccess.item);
            //pManager[0].Optional = true;

            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "SG Assembly", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Param_TB_Model(), "TBModel", "TBModel", "TBModel", GH_ParamAccess.item);

            pManager[0].Optional = true;
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            // GH_TB_Model gh_mdl = null;

            SG_Shape sg_shape = null;

            // --- input --- 
            if (!DA.GetData(0, ref sg_shape)) { return; }

            // --- solve ---

            // TB_Model mdl = gh_mdl.Value; 
            var s = sg_shape;
            TB_Model tb_mdl = new TB_Model(sg_shape);

            SolveLS slv = new SolveLS(ref tb_mdl);
            var o_mdl = new GH_TB_Model(slv.Mdl);


            // --- output ---

            DA.SetData(0, o_mdl);
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
                return null;// Properties.Resources.icons_C_Sol_LS;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("39D2D5C4-EF7D-47D4-9C2E-76A8E488998A"); }
        }
    }
}
