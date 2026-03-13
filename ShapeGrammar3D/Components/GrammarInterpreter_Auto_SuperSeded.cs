using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar.Components
{
    public class GrammarInterpreter_Auto_SS  : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GrammerInterpreter_Auto class.
        /// </summary>
        public GrammarInterpreter_Auto_SS()
          : base("GrammerInterpreter_Auto_SS", "GI_Auto_SS",
              "Automatic Grammar Interpreter",
              UT.CAT, UT.GR_INT)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "SG Assembly", GH_ParamAccess.item);
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for Automatic Interpreter", GH_ParamAccess.list);
            pManager.AddGenericParameter("Genotype", "Genotype", "Genotype/Chromosome", GH_ParamAccess.item);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "SG Assembly", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            SG_Shape iniShape = new SG_Shape();
            List<SG_Rule> rls = new List<SG_Rule>();
            SG_Genotype inigt = new SG_Genotype();

            // --- input ---
            if (!DA.GetData(0, ref iniShape)) return;
            if (!DA.GetDataList(1, rls)) return;
            if (!DA.GetData(2, ref inigt)) return;

            // --- solve ---

            // Create a deep copy
            SG_Shape shape = UT.DeepCopy(iniShape);
            SG_Genotype gt = inigt; // Util.DeepCopy(inigt);

            // Select relevant elements
            for (int i = 0; i < rls.Count; i++)
            {
                try
                {
                    string message = rls[i].RuleOperation(ref shape, ref gt);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
                }

                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    return;
                }
            }

            // --- output ---
            DA.SetData(0, shape);
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("676601C2-219B-4265-B77A-C981DB51982C"); }
        }
    }
}