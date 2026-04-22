using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GrammarInterpreter_ForManualInput : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GrammarInterpreter_ForManualInput class.
        /// </summary>
        public GrammarInterpreter_ForManualInput()
          : base("Grammar Interpreter for Manual input", "GI_Manual",
              "Grammar interpreter driven by a supplied genotype (manual / custom chromosome input).",
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

            SG_Shape shape = UT.DeepCopy(iniShape);
            SG_Genotype gt = inigt;

            rls = EnsureInitShapeFirst(rls);

            // Validate genotype: check all rule markers are present
            var missingMarkers = new List<string>();
            foreach (var rule in rls)
            {
                int sid = -999, eid = -999;
                gt.FindRange(ref sid, ref eid, rule.RuleMarker);
                if (sid == -999 || eid == -999)
                    missingMarkers.Add(string.Format("{0} (marker {1})", rule.Name, rule.RuleMarker));
            }
            if (missingMarkers.Count > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    string.Format("Genotype is missing markers for: {0}. "
                        + "Ensure all rules are also connected to CreateCustomGenotype.",
                        string.Join(", ", missingMarkers)));
            }

            for (int i = 0; i < rls.Count; i++)
            {
                try
                {
                    string message = rls[i].RuleOperation(ref shape, ref gt);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        string.Format("Rule {0} threw: {1}", rls[i].Name, ex.Message));
                    return;
                }
            }

            // --- output ---
            DA.SetData(0, shape);
        }

        private static List<SG_Rule> EnsureInitShapeFirst(List<SG_Rule> rules)
        {
            var initRules = rules.Where(r => r is SG_AutoRule_InitShape_3D).ToList();
            if (initRules.Count == 0) return rules;
            var otherRules = rules.Where(r => !(r is SG_AutoRule_InitShape_3D)).ToList();
            var sorted = new List<SG_Rule>(initRules);
            sorted.AddRange(otherRules);
            return sorted;
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