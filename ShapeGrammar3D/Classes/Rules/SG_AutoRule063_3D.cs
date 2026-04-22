using System;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule063_3D : SG_Rule
    {
        // --- properties ---
        public string ElemName { get; set; }
        public int[] Domain { get; set; }
        public double MinRatio { get; set; }

        // --- constructors ---
        public SG_AutoRule063_3D()
        {
        }

        public SG_AutoRule063_3D(string _eName, int[] _domain, double minRatio = 0.0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule063-3D";
            ElemName = _eName;
            Domain = _domain;
            MinRatio = Math.Clamp(minRatio, 0.0, 1.0);
            RuleMarker = UT.RULE063_MARKER;
        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Bottom-chord counterpart of rule061: for each RULE020 strut, the two
        /// adjacent InitShape base beams (left / right) are identified, and on
        /// each adjacent beam the strut with the closest foot is selected.
        /// Option gene: 1 = left, 2 = right, 3 = both.
        /// </summary>
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            return Rule06xShared.ApplyTipOrFootConnector(
                ss_ref,
                gt,
                UT.RULE063_MARKER,
                useTip: false,
                elemName: "3DAR63",
                label: "063",
                domain: Domain,
                minRatio: MinRatio);
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
    }
}
