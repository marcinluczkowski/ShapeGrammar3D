using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SH_RuleA : SG_Rule
    {
        // --- properties ---
        //public State RuleState = State.beta;

        // --- constructors --
        public SH_RuleA()
        {
            // empty
            RuleState = State.beta;
            Name = "SH_RuleA";
        }

        public override void NewRuleParameters(Random random, SG_Shape ss)
        {
            
        }

        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        // --- methods ---
        public override string RuleOperation(ref SG_Shape _ss)
        {
            // check if the state maches the simple shape state
            if (_ss.SimpleShapeState != RuleState)
            {
                return "RuleA is not applicable with the current state.";
            }

            // change the state
            _ss.SimpleShapeState = State.gamma;
            return "RuleA applied!";
        }
        public override State GetNextState()
        {
            return State.gamma;
        }
    }
}
