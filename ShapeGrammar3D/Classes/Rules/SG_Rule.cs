using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Describes what a rule iterates over when consuming chromosome genes.
    /// </summary>
    public enum RuleIterationTarget
    {
        /// <summary>Iterates over shape nodes (e.g. Rule02 strut generation).</summary>
        Nodes,
        /// <summary>Iterates over shape elements (e.g. Rule01 subdivision).</summary>
        Elements,
        /// <summary>Iterates over stud elements produced by earlier rules (e.g. Rule031, Rule032, Rule041).</summary>
        Studs
    }

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

        /// <summary>
        /// Describes what collection this rule iterates over when consuming genes.
        /// Override in subclasses. Default is <see cref="RuleIterationTarget.Nodes"/>.
        /// </summary>
        public virtual RuleIterationTarget IterationTarget => RuleIterationTarget.Nodes;

        /// <summary>
        /// Computes the chromosome length (number of gene slots) this rule needs
        /// for the given initial shape.  A minimum of 11 is always guaranteed.
        /// For <see cref="RuleIterationTarget.Studs"/>, the count is multiplied by
        /// the maximum <c>NumStuds</c> found on any node so that stud-consuming
        /// rules have enough genes even when Rule011 sets NumStuds &gt; 1.
        /// </summary>
        public virtual int GetChromosomeLength(SG_Shape shape)
        {
            int count;
            switch (IterationTarget)
            {
                case RuleIterationTarget.Elements:
                    count = shape?.Elems?.Count ?? 0;
                    break;
                case RuleIterationTarget.Studs:
                    int nodeCount = shape?.Nodes?.Count ?? 0;
                    int maxStuds = 1;
                    if (shape?.Nodes != null)
                    {
                        foreach (var n in shape.Nodes)
                            if (n.NumStuds > maxStuds) maxStuds = n.NumStuds;
                    }
                    count = nodeCount * maxStuds;
                    break;
                default:
                    count = shape?.Nodes?.Count ?? 0;
                    break;
            }
            return Math.Max(11, count + 2);
        }

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
