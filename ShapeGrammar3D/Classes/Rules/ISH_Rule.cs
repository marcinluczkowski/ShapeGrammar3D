using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes.Rules
{
    public interface ISH_Rule
    {
        void NewRuleParameters(Random random, SG_Shape simpleShape);
        SG_Rule CopyRule(SG_Rule rule);

        

    }
}
