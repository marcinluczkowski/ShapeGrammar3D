using System;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule064_3D : SG_Rule
    {
        // --- properties ---
        public string ElemName { get; set; }
        public int[] Domain { get; set; }
        public double MinRatio { get; set; }

        // --- constructors ---
        public SG_AutoRule064_3D()
        {
        }

        public SG_AutoRule064_3D(string _eName, int[] _domain, double minRatio = 0.0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule064-3D";
            ElemName = _eName;
            Domain = _domain;
            MinRatio = Math.Clamp(minRatio, 0.0, 1.0);
            RuleMarker = UT.RULE064_MARKER;
        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds vertical-plane diagonals between the top chord (rule061 ties) and
        /// the bottom chord (rule063 ties) that share the same InitShape base
        /// beam pair (A, B). Top and bottom ties are matched 1:1 by their sort
        /// order along A. Each matched pair produces the quadrilateral
        /// topA-topB-bottomB-bottomA with X-brace diagonals.
        ///   1 = single diagonal (topA → bottomB)
        ///   2 = both diagonals  (full X brace)
        /// </summary>
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            return Rule06xBraceShared.ApplyTopBottomBrace(
                ss_ref,
                gt,
                ruleMarker: UT.RULE064_MARKER,
                topMarker: UT.RULE061_MARKER,
                bottomMarker: UT.RULE063_MARKER,
                elemName: "3DAR64",
                label: "064",
                domain: Domain,
                minRatio: MinRatio);
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
    }
}
