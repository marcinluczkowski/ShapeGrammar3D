using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using ShapeGrammar3D.Classes.Rules;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Lightweight bundle linking a large GA run JSON file back to the
    /// in-memory <see cref="SG_Shape"/> seed and ordered rule list that
    /// produced it. Designed so a single wire from the large interpreter
    /// can feed <c>GI_LargeJson Reader</c> without re-supplying SG_Shape
    /// and Autorules separately.
    ///
    /// Holds live references (no deep copies) - the consumer is expected
    /// to clone before mutating.
    /// </summary>
    public class LargeRunContext
    {
        /// <summary>Initial SG_Shape used during the run (may be the empty
        /// seed produced internally by the boundary-driven interpreter).</summary>
        public SG_Shape IniShape { get; set; }

        /// <summary>Ordered list of rules (InitShape first), exactly as
        /// passed to <c>StructuralEvaluator.EvaluatePopulation</c>.</summary>
        public List<SG_Rule> Rules { get; set; }

        /// <summary>Path of the JSON file written by the interpreter
        /// (for diagnostics; the reader still takes the path as its own
        /// input so it can read JSON produced elsewhere).</summary>
        public string JsonPath { get; set; }

        /// <summary>Optional run id, copied from the writer for tracing.</summary>
        public string RunId { get; set; }

        public LargeRunContext()
        {
            Rules = new List<SG_Rule>();
        }
    }

    public class GH_LargeRunContext : GH_Goo<LargeRunContext>
    {
        public GH_LargeRunContext() { }
        public GH_LargeRunContext(LargeRunContext c) : base(c) { }

        public override bool IsValid => Value != null && Value.IniShape != null && Value.Rules != null;
        public override string TypeName => "Large Run Context";
        public override string TypeDescription => "Bundle of SG_Shape seed + ordered rules + JSON path produced by GI_LargeBnd";

        public override IGH_Goo Duplicate() => new GH_LargeRunContext(Value);

        public override string ToString()
        {
            if (Value == null) return "Large Run Context (null)";
            int ruleCount = Value.Rules?.Count ?? 0;
            int nodeCount = Value.IniShape?.Nodes?.Count ?? 0;
            string id = string.IsNullOrEmpty(Value.RunId) ? "-" : Value.RunId;
            return string.Format("Large Run Context (run {0}, {1} rules, seed {2} nodes)",
                id, ruleCount, nodeCount);
        }
    }

    public class Param_LargeRunContext : GH_PersistentParam<GH_LargeRunContext>
    {
        public Param_LargeRunContext() : base(
            new GH_InstanceDescription(
                "Large Run Context", "RunCtx",
                "Bundle of SG_Shape seed + ordered rules (+ JSON path) emitted by GI_LargeBnd. Feed into GI_LargeJson Reader to rebuild models without re-wiring SG_Shape and Autorules.",
                UT.CAT, UT.GR_PARAM))
        { }

        public override Guid ComponentGuid => new Guid("9B2F7A3D-4C5E-4D6F-8A0B-1C2D3E4F5A6B");
        protected override System.Drawing.Bitmap Icon => global::ShapeGrammar3D.Properties.Resources.icons_P_RunContext;

        protected override GH_GetterResult Prompt_Plural(ref List<GH_LargeRunContext> values) => GH_GetterResult.success;
        protected override GH_GetterResult Prompt_Singular(ref GH_LargeRunContext value) => GH_GetterResult.success;
    }
}
