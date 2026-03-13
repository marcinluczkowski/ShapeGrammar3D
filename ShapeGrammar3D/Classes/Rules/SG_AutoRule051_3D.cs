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

                // Order along initial curve by parameter t (ClosestPoint of strut base on curve) — not distance from start
                var targetStudsWithT = new List<(double t, SG_Node tip)>();
                double t0 = 0;
                iniCrv.ClosestPoint(stud0.Nodes[0].Pt, out t0);

                for (int j = 0; j < studElements.Count; j++)
                {
                    if (j == i) continue;

                    var stud = (SG_Elem1D)studElements[j];
                    // Parameter t from the strut end that lies on the curve (bottom); tip = other end
                    double t0n, t1n;
                    iniCrv.ClosestPoint(stud.Nodes[0].Pt, out t0n);
                    iniCrv.ClosestPoint(stud.Nodes[1].Pt, out t1n);
                    Point3d on0 = iniCrv.PointAt(t0n), on1 = iniCrv.PointAt(t1n);
                    double d0 = stud.Nodes[0].Pt.DistanceTo(on0), d1 = stud.Nodes[1].Pt.DistanceTo(on1);
                    if (d0 > UT.PRES && d1 > UT.PRES) continue; // strut not attached to this curve
                    double t = d0 <= d1 ? t0n : t1n;
                    SG_Node tip = d0 <= d1 ? stud.Nodes[1] : stud.Nodes[0];
                    targetStudsWithT.Add((t, tip));
                }

                // Previous = immediate left along curve (max t < t0), next = immediate right (min t > t0)
                var previousEntry = targetStudsWithT.Where(p => p.t < t0).OrderByDescending(p => p.t).FirstOrDefault();
                var nextEntry = targetStudsWithT.Where(p => p.t > t0).OrderBy(p => p.t).FirstOrDefault();
                var closestStuds = new List<SG_Node>();
                if (previousEntry.tip != null) closestStuds.Add(previousEntry.tip);
                if (nextEntry.tip != null) closestStuds.Add(nextEntry.tip);


                SG_Elem1D newElem0 = new SG_Elem1D();
                SG_Elem1D newElem1 = new SG_Elem1D();

                if (closestStuds.Count >= 1)
                {
                    Point3d myTip = stud0.Nodes[1].Pt;
                    var ln0 = new Line(myTip, closestStuds[0].Pt);
                    bool ln0Valid = ln0.IsValid && ln0.Length > UT.PRES;
                    if (ln0Valid)
                        newElem0 = new SG_Elem1D(ln0, -999, "3DAR5", def_crosec) { Autorule = UT.RULE051_MARKER };

                    bool ln1Valid = false;
                    if (closestStuds.Count >= 2)
                    {
                        var ln1 = new Line(myTip, closestStuds[1].Pt);
                        ln1Valid = ln1.IsValid && ln1.Length > UT.PRES;
                        if (ln1Valid)
                            newElem1 = new SG_Elem1D(ln1, -999, "3DAR5", def_crosec) { Autorule = UT.RULE051_MARKER };
                    }

                    if (!flg_start || !flg_end)
                    {
                        if (optionNumber == 1 && ln0Valid)
                            ss_ref.AddNewElement(newElem0);
                        else if (optionNumber == 2 && ln1Valid)
                            ss_ref.AddNewElement(newElem1);
                        else if (optionNumber == 3)
                        {
                            if (ln0Valid) ss_ref.AddNewElement(newElem0);
                            if (ln1Valid) ss_ref.AddNewElement(newElem1);
                        }
                    }
                }

                var o = optionNumber;



                //if (optionNumber == 2)
                //{
                //    if (newElemL.Ln.Length > UT.PRES && !flg_start_or_end)
                //    {
                //        ss_ref.AddNewElement(newElemL);
                //    }

                //}
                //else if (optionNumber == 1)
                //{
                //    if (newElemR.Ln.Length > UT.PRES && !flg_start_or_end)
                //    {
                //        ss_ref.AddNewElement(newElemR);
                //    }
                //}
                //else if (optionNumber == 3)
                //{
                //    if (newElemL.Ln.Length > UT.PRES && !flg_start_or_end)
                //    {
                //        ss_ref.AddNewElement(newElemL);
                //    }
                //    if (newElemR.Ln.Length > UT.PRES && !flg_start_or_end)
                //    {
                //        ss_ref.AddNewElement(newElemR);
                //    }
                //}


            }

            return "Auto-rule 051-3D successfully applied.";
        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }


    }
}
