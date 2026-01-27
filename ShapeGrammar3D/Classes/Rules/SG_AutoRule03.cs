using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule03 : SG_Rule
    {

        // --- properties ---
        public string ElemName { get; set; }

        // --- constructors ---
        public SG_AutoRule03()
        {
        }

        public SG_AutoRule03(string _eName)
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_03";
            ElemName = _eName;
            RuleMarker = UT.RULE030_MARKER;
        }

        // --- methods ---
        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {


            // collect R2 elements
            var selElems = ss_ref.Elems.Where(e => e.Autorule == 2);

            // create node lists separately for above and below R0/R1 elements 
            // these should exclude the supported nodes

            List<SG_Node> nds_side0 = new List<SG_Node>();
            List<SG_Node> nds_side1 = new List<SG_Node>();

            var elems = new List<SG_Elem1D>();
            // added on 250220 
            int cnt = 0;
            foreach (var e in selElems)
            {
                var nd_0 = e.Nodes[0];
                var elem = (SG_Elem1D)e;
                var line = new Line(elem.EPln.Origin, elem.EPln.YAxis, elem.Ln.Length);
                nds_side0.Add(new SG_Node(line.From, cnt));
                cnt++;
                nds_side1.Add(new SG_Node(line.To, cnt));
                cnt++;
            }

            //List<SG_Node> nds_side0 = selElems.Where(e => (e.Nodes[1].Pt.Z - e.Nodes[0].Pt.Z) > 0).Select(e => e.Nodes[1]).ToList();
            // List<SG_Node> nds_side1 = selElems.Where(e => (e.Nodes[1].Pt.Z - e.Nodes[0].Pt.Z) < 0).Select(e => e.Nodes[1]).ToList(); 

            // additioally add supported nodes to each list. 
            List<SG_Node> supNds = ss_ref.Nodes.Where(n => n.Support.SupportCondition > 0).ToList();

            foreach (var sn in supNds)
            {
                var r2Elem_from_supNds = sn.Elements.Where(e => e.Autorule == 2);

                if (r2Elem_from_supNds.Count() == 0) // case with no r2 elems on the support pts
                {
                    nds_side0.Add(sn);
                    nds_side1.Add(sn);
                }

                else
                // in case there is a R2 elem from the support
                // the node should be added only in the list opposite side of the R2 member. 
                {
                    var r2_elem = r2Elem_from_supNds.First();

                    if (r2_elem.Nodes[1].Pt.Z - r2_elem.Nodes[0].Pt.Z > 0)
                    {
                        nds_side1.Add(sn);
                    }
                    else
                    {
                        nds_side0.Add(sn);
                    }
                }
            }

            List<SG_Node> nds_side0_sorted = nds_side0.OrderBy(n => n.Pt.X).ToList();
            List<SG_Node> nds_side1_sorted = nds_side1.OrderBy(n => n.Pt.X).ToList();

            for (int i = 0; i < nds_side0_sorted.Count - 1; i++)
            {
                SG_Node nd0 = nds_side0_sorted[i];
                SG_Node nd1 = nds_side0_sorted[i + 1];

                if (nd0.Support.SupportCondition > 0 && nd1.Support.SupportCondition > 0)
                {
                    continue;
                }

                SG_Node[] nds = new SG_Node[2] { nd0, nd1 };
                SG_Elem1D newElem = new SG_Elem1D(nds, -999, ElemName) { Autorule = 3 };

                ss_ref.AddNewElement(newElem);
            }

            for (int i = 0; i < nds_side1_sorted.Count - 1; i++)
            {
                SG_Node nd0 = nds_side1_sorted[i];
                SG_Node nd1 = nds_side1_sorted[i + 1];

                if (nd0.Support.SupportCondition > 0 && nd1.Support.SupportCondition > 0)
                {
                    continue;
                }

                SG_Node[] nds = new SG_Node[2] { nd0, nd1 };
                SG_Elem1D newElem = new SG_Elem1D(nds, -999, ElemName) { Autorule = 3 };

                ss_ref.AddNewElement(newElem);
            }

            // add R2 elems and split existent R3 elems
            // 2023.08.24

            List<int> removeIds = new List<int>();
            var initialR3s = ss_ref.Elems.Where(e => e.Autorule == 3).ToList();
            foreach (SG_Elem1D r3e in initialR3s)
            {
                var selNds = ss_ref.Nodes
                    .Where(n => n.Pt.X > r3e.Nodes[0].Pt.X && n.Pt.X < r3e.Nodes[1].Pt.X)
                    .Where(n => n.Elements.Where(e => e.Autorule == 1).Count() != 0)
                    .OrderBy(n => n.Pt.X)
                    .ToList();

                var lastNode = new SG_Node();
                for (var i = 0; i < selNds.Count(); i++)
                {
                    var nd = selNds[i];

                    // find intersection
                    var vL = new Line(nd.Pt, Vector3d.ZAxis);
                    Intersection.LineLine(r3e.Ln, vL, out double param, out _);

                    // add intermediate node
                    SG_Node middle_nd = SG_Node.CreateNode(r3e, param, ss_ref.nodeCount);
                    ss_ref.Nodes.Add(middle_nd);
                    ss_ref.nodeCount++;

                    var left_nd = new SG_Node();
                    var right_nd = new SG_Node();
                    bool bl_left = false;
                    bool bl_right = false;
                    if (i == 0 && i == selNds.Count() - 1)
                    {
                        left_nd = r3e.Nodes[0];
                        right_nd = r3e.Nodes[1];
                        bl_left = true;
                        bl_right = true;
                    }
                    else if (i == 0)
                    {
                        left_nd = r3e.Nodes[0];
                        bl_left = true;
                    }
                    else if (i == selNds.Count() - 1)
                    {
                        left_nd = lastNode;
                        right_nd = r3e.Nodes[1];
                        bl_left = true;
                        bl_right = true;
                    }
                    else
                    {
                        left_nd = lastNode;
                        bl_left = true;
                    }

                    // create one vertical elem and two R3 elem by splitting the initial R3 elem
                    SG_Elem1D newVL = new SG_Elem1D(new SG_Node[2] { nd, middle_nd }, ss_ref.elementCount, "AR2") { Autorule = 3 };
                    ss_ref.Elems.Add(newVL);
                    ss_ref.elementCount++;

                    if (bl_left)
                    {
                        SG_Elem1D newR3_0 = new SG_Elem1D(new SG_Node[2] { left_nd, middle_nd }, ss_ref.elementCount, r3e.Name) { Autorule = 3 };
                        ss_ref.Elems.Add(newR3_0);
                        ss_ref.elementCount++;
                    }

                    if (bl_right)
                    {
                        SG_Elem1D newR3_1 = new SG_Elem1D(new SG_Node[2] { middle_nd, right_nd }, ss_ref.elementCount, r3e.Name) { Autorule = 3 };
                        ss_ref.Elems.Add(newR3_1);
                        ss_ref.elementCount++;
                    }

                    // register this turn's midnode for the next use
                    lastNode = middle_nd;

                }
                // remove element just split 
                if (selNds.Count() != 0)
                {
                    removeIds.Add(r3e.ID);
                }

            }

            ss_ref.Elems = ss_ref.Elems.Where(e => removeIds.Contains(e.ID) == false).ToList();

            return "Auto-rule 03 successfully applied.";
        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }

        // private void 
    }
}
