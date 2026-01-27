using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public abstract class SG_Rule : ISH_Rule
    {
        public State RuleState;
        public string Name;
        public int RuleMarker;

        public SG_Rule()
        { 
            
        }

        public abstract void NewRuleParameters(Random random, SG_Shape ss);
        public abstract SG_Rule CopyRule(SG_Rule rule);

        public virtual string RuleOperation(ref SG_Shape _ss) { return ""; }
        public virtual string RuleOperation(ref SG_Shape _ss, ref SG_Genotype _st) { return ""; }

        public abstract State GetNextState();

        // for child class
        //public override void NewRuleParameters(Random random, SG_Shape ss) { }
        //public override SG_Rule CopyRule(SG_Rule rule)
        //{
        //    throw new NotImplementedException();
        //}
        //public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        //{
        //    throw new NotImplementedException();
        //}
        //public override State GetNextState()
        //{
        //    throw new NotImplementedException();
        //}
    }


}
