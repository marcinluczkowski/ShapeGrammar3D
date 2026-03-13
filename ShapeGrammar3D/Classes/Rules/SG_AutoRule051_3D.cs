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
    public class SG_AutoRule051_3D : SG_Rule
    {

        // --- properties ---
        public string ElemName { get; set; }
        // public int Option { get; set; }
        public int[] Domain { get; set; }


        // --- constructors ---
        public SG_AutoRule051_3D()
        {
        }

        public SG_AutoRule051_3D(string _eName, int[] _domain)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule051-3D";
            ElemName = _eName;
            //Option = _opt;
            Domain = _domain;

            RuleMarker = UT.RULE051_MARKER;

        }

        // --- methods ---
        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            // algorithm for rule 05-3d
            SH_CrossSection_Beam def_crosec = ss_ref.Elems?
                .OfType<SG_Elem1D>()
                .FirstOrDefault()?.CrossSection;
            if (def_crosec == null)
            {
                var fallback = new SH_CrossSection_Rectangle(10, 10);
                fallback.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
                def_crosec = fallback;
            }
            // find relevant range in genotype
            int sid = -999;
            int eid = -999;
            List<int> selectedIntGenes;
            List<double> selectedDGenes;

            gt.FindRange(ref sid, ref eid, UT.RULE051_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule051-3D - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            double range = Domain[1] - Domain[0];

            var studElements = new List<SG_Element>();
            for (int i = 0; i < ss_ref.Elems.Count; i++)
            {
                if (ss_ref.Elems[i].Name == "3DAR2")
                {
                    studElements.Add(ss_ref.Elems[i]);
                }
            }

            var initialElems = ss_ref.Elems.Where(e => e.Autorule == UT.RULE010_MARKER).ToList();

            for (int i = 0; i < selectedIntGenes.Count; i++)
            {
                bool flg_start_or_end = false;
                bool flg_start = false;
                bool flg_end = false;

                if (i >= studElements.Count) break;
                if (selectedIntGenes[i] == 0) continue;

                double optionDbl = selectedDGenes[i] * range + Domain[0];
                double roundedOptDbl = Math.Round(optionDbl, 0);
                int optionNumber = (int)roundedOptDbl;

                var stud0 = (SG_Elem1D)studElements[i];

                var iniCrv = ((SG_Elem1D)stud0.Nodes[0].Elements.Where(e => e.Autorule == UT.RULE010_MARKER).ToList()[0]).Init_Crv;

                if (iniCrv.PointAtStart.DistanceTo(stud0.Nodes[0].Pt) < UT.PRES)
                {
                    flg_start = true;
                }

                if (iniCrv.PointAtEnd.DistanceTo(stud0.Nodes[0].Pt) < UT.PRES)
                {
                    flg_end = true;
                }


                if (iniCrv.PointAtStart.DistanceTo(stud0.Nodes[0].Pt) < UT.PRES ||
                    iniCrv.PointAtEnd.DistanceTo(stud0.Nodes[0].Pt) < UT.PRES)
                {
                    flg_start_or_end = true;
                }

                var targetStudTipNodes = new List<SG_Node>();
                for (int j = 0; j < studElements.Count; j++) 
                {
                    if (j == i) continue; // skip the current stud to avoid zero-length lines

                    var stud = (SG_Elem1D)studElements[j];

                    // if (stud.Crv.PointAtStart.CompareTo(iniCrv.PointAtStart) == 0) 
                    double t;
                    iniCrv.ClosestPoint(stud.Crv.PointAtStart, out t);

                    if (iniCrv.PointAt(t).CompareTo(stud.Crv.PointAtStart) == 0)
                    {
                        targetStudTipNodes.Add(stud.Nodes[1]);
                    }

                    else if (iniCrv.PointAt(t).CompareTo(stud.Crv.PointAtEnd) == 0)
                    {
                        targetStudTipNodes.Add(stud.Nodes[0]);
                    }

                    else
                    { 
                    
                    }

                }

                var closestStuds = targetStudTipNodes.OrderBy(n => n.Pt.DistanceTo(stud0.Nodes[1].Pt)).ToList();


                SG_Elem1D newElem0 = new SG_Elem1D();
                SG_Elem1D newElem1 = new SG_Elem1D();

                if (closestStuds.Count > 2)
                {
                    var ln0 = new Line(stud0.Nodes[1].Pt, closestStuds[0].Pt);
                    var ln1 = new Line(stud0.Nodes[1].Pt, closestStuds[1].Pt);

                    bool ln0Valid = ln0.IsValid && ln0.Length > UT.PRES;
                    bool ln1Valid = ln1.IsValid && ln1.Length > UT.PRES;

                    if (ln0Valid)
                        newElem0 = new SG_Elem1D(ln0, -999, "3DAR5", def_crosec) { Autorule = UT.RULE051_MARKER };
                    if (ln1Valid)
                        newElem1 = new SG_Elem1D(ln1, -999, "3DAR5", def_crosec) { Autorule = UT.RULE051_MARKER };

                    if (!flg_start_or_end)
                    {
                        if (optionNumber == 1 && ln0Valid)
                        {
                            ss_ref.AddNewElement(newElem0);
                        }
                        else if (optionNumber == 2 && ln1Valid)
                        {
                            ss_ref.AddNewElement(newElem1);
                        }
                        else if (optionNumber == 3)
                        {
                            if (ln0Valid) ss_ref.AddNewElement(newElem0);
                            if (ln1Valid) ss_ref.AddNewElement(newElem1);
                        }
                    }
                    else
                    {
                        if (ln0Valid) ss_ref.AddNewElement(newElem0);
                    }
                }

                else if (closestStuds.Count == 2)
                {
                    var ln0 = new Line(stud0.Nodes[1].Pt, closestStuds[0].Pt);
                    if (!ln0.IsValid || ln0.Length <= UT.PRES) continue;

                    newElem0 = new SG_Elem1D(ln0, -999, "3DAR5", def_crosec) { Autorule = UT.RULE051_MARKER };

                    if (optionNumber == 1)
                    {
                        ss_ref.AddNewElement(newElem0);
                    }
                    else if (optionNumber == 2)
                    {
                        ss_ref.AddNewElement(newElem0);
                    }
                    else if (optionNumber == 3)
                    {
                        ss_ref.AddNewElement(newElem0);
                    }
                }

            }

            return "Auto-rule 051-3D successfully applied.";
        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }


    }
}
