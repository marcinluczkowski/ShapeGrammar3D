using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShapeGrammar3D.Classes.Elements;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;


namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule04 : SG_Rule
    {

        // --- properties ---
        public string ElemName { get; set; }

        // --- constructors ---
        public SG_AutoRule04()
        {
        }

        public SG_AutoRule04(string _eName)
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_04";
            ElemName = _eName;
            RuleMarker = UT.RULE040_MARKER;

        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

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

            gt.FindRange(ref sid, ref eid, UT.RULE040_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule04 - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            // rule 4 content

            var r2_elms = ss_ref.Elems.Where(e => e.Name == "AR2").ToList();

            var r2_elms_upp = r2_elms.Where(e => e.Nodes[1].Pt.Z - e.Nodes[0].Pt.Z > 0).OrderBy(e => e.Nodes[0].Pt.X).ToList();
            var r2_elms_low = r2_elms.Where(e => e.Nodes[1].Pt.Z - e.Nodes[0].Pt.Z < 0).OrderBy(e => e.Nodes[0].Pt.X).ToList();

            int gene_cnt = 0;
            CreatesDiagonals(ref ss_ref, selectedIntGenes, r2_elms_upp, ref gene_cnt);
            CreatesDiagonals(ref ss_ref, selectedIntGenes, r2_elms_low, ref gene_cnt);

            // remove unused nodes
            ss_ref.UnregisterElemsFromNodes();
            ss_ref.RegisterElemsToNodes();
            ss_ref.RemoveUnusedNodes();

            return "Auto-rule 04 successfully applied.";
        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }

        internal void CreatesDiagonals(ref SG_Shape ss_ref, List<int> _IntGenes, List<SG_Element> _r2_elms, ref int cnt)
        {
            for (var i = 0; i < _r2_elms.Count() - 1; i++)
            {
                // terminates if gene count is not sufficient
                if (cnt >= _IntGenes.Count()) break;
                // moves on if the corresponding gene value is zero
                if (_IntGenes[cnt] == 0) 
                {
                    cnt++;
                    continue; 
                }

                // relevant r2 elms to create diagonals
                var current_r2e = _r2_elms[i];
                var next_r2e = _r2_elms[i + 1];

                // check whether any supports exist between the two r2 elems. Support can contain a r2 elem on the opposite
                // side, and thus s.Node.Elements.Where(e => e.Autorule == 2).Count() <= 1
                var supNds_inbtw = ss_ref.Supports
                    .Where(s => s.Pt.X > current_r2e.Nodes[0].Pt.X 
                             && s.Pt.X < next_r2e.Nodes[0].Pt.X)
                    .Where(s => s.Node.Elements.Where(e => e.Name == "AR2").Count() <= 1);

                // don't create diagonals and continue if there are such supports
                if (supNds_inbtw.Count() != 0) 
                {
                    continue;
                }

                // otherwise, creates diagonals
                var e0n0 = _r2_elms[i].Nodes[0];
                var e0n1 = _r2_elms[i].Nodes[1];
                var e1n0 = _r2_elms[i + 1].Nodes[0];
                var e1n1 = _r2_elms[i + 1].Nodes[1];

                // create 2x Elements
                SG_Elem1D newLn0 = new SG_Elem1D(new SG_Node[] { e0n0, e1n1 }, ss_ref.elementCount, "dg") { Autorule = 4 };
                SG_Elem1D newLn1 = new SG_Elem1D(new SG_Node[] { e0n1, e1n0 }, ss_ref.elementCount + 1, "dg") { Autorule = 4 };
                ss_ref.elementCount += 2;

                ss_ref.Elems.AddRange(new List<SG_Element>(2) { newLn0, newLn1 });
                cnt++;
            }

        }

    }
}
