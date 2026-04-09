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
    public class SG_AutoRule060_3D : SG_Rule
    {

        // --- properties ---
        public string ElemName { get; set; }
        public int[] Domain { get; set; }

        // --- constructors ---
        public SG_AutoRule060_3D()
        {
        }

        //public SG_AutoRule060_3D(string _eName, int[] _domain)
        public SG_AutoRule060_3D(string _eName)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule060-3D";
            ElemName = _eName;
            // Domain = _domain;

            RuleMarker = UT.RULE060_MARKER;

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
            SH_CrossSection_Beam def_crosec = ss_ref.Elems?
                .OfType<SG_Elem1D>()
                .FirstOrDefault()?.CrossSection
                ?? new SH_CrossSection_Beam();

            // find relevant range in genotype
            int sid = -999;
            int eid = -999;
            List<int> selectedIntGenes;
            List<double> selectedDGenes;

            gt.FindRange(ref sid, ref eid, UT.RULE060_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule060-3D - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            var studElements = new List<SG_Element>();
            for (int i = 0; i < ss_ref.Elems.Count; i++)
            {
                if (ss_ref.Elems[i].Name == "3DAR2")
                {
                    studElements.Add(ss_ref.Elems[i]);
                }
            }

            RhinoApp.WriteLine("Total Stud Elements {0}", studElements.Count.ToString());

            var initialElems = new List<SG_Element>();
            if (studElements != null && studElements.Count != 0)
            {
                initialElems = ss_ref.Elems.Where(e => e.Autorule == UT.RULE010_MARKER).ToList();
            }

            int pairCount = Math.Min(selectedIntGenes.Count, studElements.Count);
            if (pairCount == 0)
            {
                return "Autorule060-3D - no stud elements available";
            }

            for (int i = 0; i < pairCount; i++)
            {
                var stud0 = (SG_Elem1D)studElements[i];

                var baseElem0 = stud0.Nodes[0].Elements
                    .Where(e => e.Autorule == UT.RULE010_MARKER)
                    .OfType<SG_Elem1D>()
                    .FirstOrDefault();
                if (baseElem0 == null) continue;
                var iniCrv = baseElem0.Init_Crv;
                if (iniCrv == null) continue;

                var targetElements = new List<SG_Element>();
                for (int j = 0; j < studElements.Count; j++)
                {
                    var stud1 = (SG_Elem1D)studElements[j];
                    var baseElem1 = stud1.Nodes[0].Elements
                        .Where(e => e.Autorule == UT.RULE010_MARKER)
                        .OfType<SG_Elem1D>()
                        .FirstOrDefault();
                    if (baseElem1 == null) continue;
                    var iniCrv1 = baseElem1.Init_Crv;
                    if (iniCrv1 == null) continue;

                    if (iniCrv.PointAtStart.CompareTo(iniCrv1.PointAtStart) != 0)
                    {
                        targetElements.Add(stud1);
                        RhinoApp.WriteLine("Added element as target.");
                    }
                    else
                    {
                        RhinoApp.WriteLine("Skipped element due to orientation or length. {0}", iniCrv.PointAtStart.CompareTo(iniCrv1.PointAtStart).ToString());
                        RhinoApp.WriteLine("iniCrv.PointAtStart: {0}", iniCrv.PointAtStart.ToString());
                        RhinoApp.WriteLine("iniCrv1.PointAtStart: {0}", iniCrv1.PointAtStart.ToString());
                    }
                }

                Rhino.RhinoApp.WriteLine("numTargetElems {0}", targetElements.Count.ToString());

                if (targetElements.Count == 0)
                {
                    RhinoApp.WriteLine("No target elements found for stud index {0}; skipping.", i);
                    continue;
                }

                var targetStud = targetElements.OrderBy(t => t.Nodes[1].Pt.DistanceTo(stud0.Nodes[1].Pt)).ToList()[0];

                var brLine = new Line(stud0.Nodes[1].Pt, targetStud.Nodes[1].Pt);
                if (!brLine.IsValid || brLine.Length < UT.PRES) continue;
                var newBeam = new SG_Elem1D(brLine, -999, "3DAR5", def_crosec) { Autorule = UT.RULE060_MARKER };

                ss_ref.AddNewElement(newBeam);
            }

            return "Auto-rule 060-3D successfully applied.";
        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }


    }
}
