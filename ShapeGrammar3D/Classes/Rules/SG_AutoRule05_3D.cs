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
    public class SG_AutoRule05_3D : SG_Rule
    {

        // --- properties ---
        public string ElemName { get; set; }
        // public int Option { get; set; }
        public int[] Domain { get; set; }

        // --- constructors ---
        public SG_AutoRule05_3D()
        {
        }

        public SG_AutoRule05_3D(string _eName, int[] _domain)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule05-3D";
            ElemName = _eName;
            //Option = _opt;
            Domain = _domain;

            RuleMarker = UT.RULE050_MARKER;

        }

        // --- methods ---
        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            SH_CrossSection_Beam def_crosec = ss_ref.Elems?
                .OfType<SG_Elem1D>()
                .FirstOrDefault()?.CrossSection
                ?? new SH_CrossSection_Beam();

            // find relevant range in genotype
            int sid = -999;
            int eid = -999;
            List<int> selectedIntGenes;
            List<double> selectedDGenes;

            gt.FindRange(ref sid, ref eid, UT.RULE050_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule05-3D - wrong marker";
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

            var initialElems = relevantElems; 

            for (int i = 0; i < selectedIntGenes.Count; i++)
            {
                if (i >= relevantElems.Count) break;
                if (selectedIntGenes[i] == 0) continue;

                double optionDbl = selectedDGenes[i] * range + Domain[0];
                double roundedOptDbl = Math.Round(optionDbl, 0);
                int optionNumber = (int)roundedOptDbl;

                var re = (SG_Elem1D) relevantElems[i];

                var closestElms = initialElems.OrderBy(e => e.Nodes[1].Pt.DistanceTo(re.Nodes[1].Pt)).ToList();

                Point3d tip = re.Nodes[1].Pt;
                Point3d rightElemPt, leftElemPt = new Point3d();
                Line line = new Line();

                if (closestElms.Count == 0) { }
                else if (closestElms.Count == 1 && (optionNumber == 1 || optionNumber == 3))
                {
                    rightElemPt = closestElms[0].Nodes[1].Pt;

                    line = new Line(tip, rightElemPt);
                    SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR5", def_crosec) { Autorule = 5 };
                    ss_ref.AddNewElement(newElem);

                }
                else if (closestElms.Count > 1)
                {
                    rightElemPt = closestElms[0].Nodes[1].Pt;
                    leftElemPt = closestElms[1].Nodes[1].Pt;
                    
                    line = new Line(tip, rightElemPt);
                    SG_Elem1D newElemR = new SG_Elem1D(line, -999, "3DAR5", def_crosec) { Autorule = UT.RULE050_MARKER };
                    
                    line = new Line(tip, leftElemPt);
                    SG_Elem1D newElemL = new SG_Elem1D(line, -999, "3DAR5", def_crosec) { Autorule = UT.RULE050_MARKER };

                    if (optionNumber == 2)
                    {
                        if (newElemL.Ln.Length > UT.PRES)
                        {
                            ss_ref.AddNewElement(newElemL);
                        }
                        
                    }
                    else if (optionNumber == 1)
                    {
                        if (newElemR.Ln.Length > UT.PRES)
                        {
                            ss_ref.AddNewElement(newElemR);
                        }
                    }
                    else if (optionNumber == 3)
                    {
                        if (newElemL.Ln.Length > UT.PRES)
                        {
                            ss_ref.AddNewElement(newElemL);
                        }
                        if (newElemR.Ln.Length > UT.PRES)
                        {
                            ss_ref.AddNewElement(newElemR);
                        }
                    }
                    
                    
                }



                //// var rightElems = closestElms[0]; // initialElems.
                //     // Where(e => e.Nodes[0].Pt.DistanceTo(re.Nodes[0].Pt) < UT.PRES).ToList();
                //// var leftElems = closestElms[1]; //  initialElems.
                //// Where(e => e.Nodes[1].Pt.DistanceTo(re.Nodes[0].Pt) < UT.PRES).ToList();

                ////Point3d tip = re.Nodes[1].Pt;
                ////Point3d rightElemPt, leftElemPt = new Point3d();
                ////Line line = new Line();
                //if (rightElems.Count != 0)
                //{
                //    rightElemPt = rightElems[0].Nodes[1].Pt;
                //    if (Option == 2 || Option == 3)
                //    {
                //        line = new Line(tip, rightElemPt);
                //        SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR5", new SH_CrossSection_Beam()) { Autorule = 4 };
                //        ss_ref.AddNewElement(newElem);
                //    }
                //}
                //if (leftElems.Count != 0)
                //{
                //    leftElemPt = leftElems[0].Nodes[0].Pt;
                //    if (Option == 1 || Option == 3)
                //    {
                //        line = new Line(tip, leftElemPt);
                //        SG_Elem1D newElem = new SG_Elem1D(line, -999, "3DAR5", new SH_CrossSection_Beam()) { Autorule = 4 };
                //        ss_ref.AddNewElement(newElem);
                //    }
                //}

            }

            return "Auto-rule 05-3D successfully applied.";
        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }


    }
}
