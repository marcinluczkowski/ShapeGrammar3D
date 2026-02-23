using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Rhino.Geometry;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule01_3D : SG_Rule
    {
        // --- properties ---
        public List<string> ElemNames { get; set; } = new List<string>();
        private readonly double[] bounds = { 0.2, 0.8 };

        // --- constructors --- 
        public SG_AutoRule01_3D()
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_01_3D";

            RuleMarker = UT.RULE010_MARKER;
        }

        public SG_AutoRule01_3D(List<string> _eNames)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_01_3D";
            ElemNames = _eNames;

            RuleMarker = UT.RULE010_MARKER;

            // T = _t;

        }

        // --- methods ---
        // methods of parent class
        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            // find relevant range in genotype
            int sid = -999;
            int eid = -999;
            List<int> selectedIntGenes;
            List<double> selectedDGenes;

            gt.FindRange(ref sid, ref eid, UT.RULE010_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule010_3D - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            List<int> removeIds = new List<int>();
            for (int i = 0; i < selectedIntGenes.Count; i++)
            {
                if (selectedIntGenes[i] == 0) continue;
                if (i >= ss_ref.Elems.Count) break;

                SG_Elem1D elem = ss_ref.Elems[i] as SG_Elem1D;
                double param = selectedDGenes[i];
                if (param < bounds[0])
                {
                    param = bounds[0];
                }
                else if (param > bounds[1])
                {
                    param = bounds[1];
                }

                Interval i1 = elem.Crv.Domain;
                Interval new_i1 = new Interval(i1.Min, i1.ParameterAt(param));

                Interval i2 = elem.Crv.Domain;
                Interval new_i2 = new Interval(i1.ParameterAt(param), i1.Max);

                double seglen1 = elem.Crv.GetLength(i1);
                double seglen2 = elem.Crv.GetLength(i2);

                if (seglen1 < UT.MIN_SEG_LEN || seglen2 < UT.MIN_SEG_LEN)
                {
                    ss_ref.Elems = ss_ref.Elems.Where(e => removeIds.Contains(e.ID) == false).ToList();
                    return "Segments are too short for Autorule01_3D.";
                }

                // add intermediate node
                SG_Node midNode = SG_Node.CreateNodeOnCrv(elem, param, ss_ref.nodeCount);
                ss_ref.Nodes.Add(midNode);
                ss_ref.nodeCount++;

                // create 2x Element
                SG_Elem1D newCrv0 = new SG_Elem1D(new SG_Node[2] { elem.Nodes[0], midNode }, elem.Crv.Split(i1.ParameterAt(param))[0], elem.Init_Crv,  ss_ref.elementCount, elem.Name, elem.CrossSection) { Autorule = UT.RULE010_MARKER };
                SG_Elem1D newCrv1 = new SG_Elem1D(new SG_Node[] { midNode, elem.Nodes[1] }, elem.Crv.Split(i1.ParameterAt(param))[1], elem.Init_Crv, ss_ref.elementCount+1, elem.Name, elem.CrossSection) { Autorule = UT.RULE010_MARKER };

                ss_ref.elementCount += 2;

                // remove Element just split
                removeIds.Add(elem.ID);
                ss_ref.Elems.AddRange(new List<SG_Element>() { newCrv0, newCrv1 });
            }

            ss_ref.Elems = ss_ref.Elems.Where(e => removeIds.Contains(e.ID) == false).ToList();

            return "Auto-rule 01_3D successfully applied.";

        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
        // methods of this class
    }
}
