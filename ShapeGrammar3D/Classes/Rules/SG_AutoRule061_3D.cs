using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule061_3D : SG_Rule
    {
        // --- properties ---
        public string ElemName { get; set; }
        public int[] Domain { get; set; }
        public double MinRatio { get; set; }

        // --- constructors ---
        public SG_AutoRule061_3D()
        {
        }

        public SG_AutoRule061_3D(string _eName, int[] _domain, double minRatio = 0.0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule061-3D";
            ElemName = _eName;
            Domain = _domain;
            MinRatio = Math.Clamp(minRatio, 0.0, 1.0);
            RuleMarker = UT.RULE061_MARKER;
        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Top-chord connector. For every RULE020 strut, only the TWO adjacent
        /// InitShape base beams (one on each side of the focus base beam) are
        /// considered. On each adjacent base beam, the strut whose tip is closest
        /// to the focus strut tip is taken as the candidate. The option gene
        /// selects which side(s) to link: 1 = left, 2 = right, 3 = both.
        /// </summary>
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            return Rule06xShared.ApplyTipOrFootConnector(
                ss_ref,
                gt,
                UT.RULE061_MARKER,
                useTip: true,
                elemName: "3DAR61",
                label: "061",
                domain: Domain,
                minRatio: MinRatio);
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
    }
}
