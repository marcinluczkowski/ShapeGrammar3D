using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule062_3D : SG_Rule
    {
        // --- properties ---
        public string ElemName { get; set; }
        public int[] Domain { get; set; }
        public double MinRatio { get; set; }

        // --- constructors ---
        public SG_AutoRule062_3D()
        {
        }

        public SG_AutoRule062_3D(string _eName, int[] _domain, double minRatio = 0.0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule062-3D";
            ElemName = _eName;
            Domain = _domain;
            MinRatio = Math.Clamp(minRatio, 0.0, 1.0);
            RuleMarker = UT.RULE062_MARKER;
        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds diagonal braces within the top-chord plane defined by the rule061
        /// ties. For every pair of InitShape base beams (A, B) that are linked by
        /// rule061 ties, the ties are sorted along A and consecutive tie pairs
        /// (tie_i, tie_{i+1}) form a quadrilateral A_i-B_i-B_{i+1}-A_{i+1}. X-brace
        /// diagonals are inserted according to the option value:
        ///   1 = single diagonal (A_i → B_{i+1})
        ///   2 = both diagonals (full X brace)
        /// At the boundary, a virtual tie joining the endpoints of A and B is used
        /// to brace the first/last rule061 tie.
        /// </summary>
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            return Rule06xBraceShared.ApplyTieBrace(
                ss_ref,
                gt,
                ruleMarker: UT.RULE062_MARKER,
                sourceMarker: UT.RULE061_MARKER,
                useTip: true,
                elemName: "3DAR62",
                label: "062",
                domain: Domain,
                minRatio: MinRatio);
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
    }
}
