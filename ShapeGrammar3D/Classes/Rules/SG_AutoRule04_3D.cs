using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule040_3D : SG_Rule
    {

        // --- properties ---
        public string ElemName { get; set; }
        // public int Option { get; set; }
        public int[] Domain { get; set; }

        // --- constructors ---
        public SG_AutoRule040_3D()
        {
        }

        public SG_AutoRule040_3D(string _eName, int[] _domain)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule04-3D";
            ElemName = _eName;
            //Option = _opt;
            Domain = _domain;
            RuleMarker = UT.RULE040_MARKER;

        }

        // --- methods ---
        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            // algorithm for rule 04-3d

            // find relevant range in genotype
            int sid = -999;
            int eid = -999;
            List<int> selectedIntGenes;
            List<double> selectedDGenes;

            gt.FindRange(ref sid, ref eid, UT.RULE040_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule04-3D - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            double range = Domain[1] - Domain[0];

            var relevantElems = new List<SG_Element>();
            for (int i = 0; i < ss_ref.Elems.Count; i++)
            {
                if (ss_ref.Elems[i].Name == "3DAR2")
                {
                    relevantElems.Add(ss_ref.Elems[i]);
                }

            }

            // rule no 41 


            var initialNodes = new List<SG_Node>();
            var initialElems = ss_ref.Elems.Where(e => e.Autorule == UT.RULE010_MARKER).ToList();

            for (int i=0; i < selectedIntGenes.Count; i++)
            {

                if (i >= relevantElems.Count) break;
                if (selectedIntGenes[i] == 0) continue;

                double optionDbl = selectedDGenes[i] * range + Domain[0];
                double roundedOptDbl = Math.Round(optionDbl, 0);
                int optionNumber = (int) roundedOptDbl;

                var re = (SG_Elem1D) relevantElems[i];
                var rightElems = initialElems.
                    Where(e => e.Nodes[0].Pt.DistanceTo(re.Nodes[0].Pt) < UT.PRES).ToList();
                var leftElems = initialElems.
                    Where(e => e.Nodes[1].Pt.DistanceTo(re.Nodes[0].Pt) < UT.PRES).ToList();

                Point3d tip = re.Nodes[1].Pt;
                Point3d rightElemPt, leftElemPt = new Point3d();
                Line line = new Line();
                if (rightElems.Count != 0)
                {
                    rightElemPt = rightElems[0].Nodes[1].Pt;
                    if (optionNumber == 2 || optionNumber == 3)
                    {
                       line = new Line(tip, rightElemPt);
                       SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
                       ss_ref.AddNewElement(newElem);
                    }
                }
                if (leftElems.Count != 0)
                {
                    leftElemPt = leftElems[0].Nodes[0].Pt;
                    if (optionNumber == 1 || optionNumber == 3)
                    {
                        line = new Line(tip, leftElemPt);
                        SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
                        ss_ref.AddNewElement(newElem);
                    }
                }

                

                //if (Option == 1)
                //{

                //    line = new Line(leftElemPt, node_tip.Pt);
                //    SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
                //    ss_ref.AddNewElement(newElem);
                //}

                //else if (Option == 2)
                //{
                //    line = new Line(node_tip.Pt, right_node.Pt);
                //    SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
                //    ss_ref.AddNewElement(newElem);
                //}

                //else if (Option == 4)
                //{
                //    line = new Line(left_node.Pt, node_tip.Pt);
                //    SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
                //    ss_ref.AddNewElement(newElem);
                //    line = line = new Line(node_tip.Pt, right_node.Pt);
                //    newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
                //    ss_ref.AddNewElement(newElem);
                //}


            // }


            //for (int i = 0; i < initialElems.Count; i++)
            //{
            //    var e = initialElems[i];
            //    initialNodes.Add(e.Nodes[0]);

            //    if (i == initialElems.Count - 1)
            //    {
            //        initialNodes.Add(e.Nodes[1]);
            //    }
            //}

            //for (int i = 0; i < selectedIntGenes.Count; i++)
            //{
            //    if (selectedIntGenes[i] == 0) continue;
            //    if (i >= relevantElems.Count) break;

            //    var node_tip = relevantElems[i].Nodes[1];

            //    SG_Node left_node, right_node;

            //    if (i == 0)
            //    {
            //        left_node = initialNodes[0];
            //    }
            //    else
            //    {
            //        left_node = relevantElems[i - 1].Nodes[0];
            //    }

            //    if (i == selectedIntGenes.Count - 1)
            //    {
            //        right_node = initialNodes[initialNodes.Count-1];
            //    }
            //    else 
            //    { 
            //        right_node = relevantElems[i + 1].Nodes[0];
            //    }

            //    Line line = new Line();

            //    if (Option == 1 )
            //    {
            //        line = new Line(left_node.Pt, node_tip.Pt);
            //        SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
            //        ss_ref.AddNewElement(newElem);
            //    }

            //    else if (Option == 2 )
            //    {
            //        line = new Line(node_tip.Pt, right_node.Pt);
            //        SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
            //        ss_ref.AddNewElement(newElem);
            //    }

            //    else if (Option == 4)
            //    {
            //        line = new Line(left_node.Pt, node_tip.Pt);
            //        SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
            //        ss_ref.AddNewElement(newElem);
            //        line = line = new Line(node_tip.Pt, right_node.Pt);
            //        newElem = new SG_Elem1D(line, -999, "3DAR4", new SH_CrossSection_Beam()) { Autorule = 4 };
            //        ss_ref.AddNewElement(newElem);
            //    }


                //SG_Elem1D newElem = new SG_Elem1D(ln, -999, "3DAR2", new SH_CrossSection_Beam()) { Autorule = 2 }


            }

            return "Auto-rule 04-3D successfully applied.";
        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }


    }
}
