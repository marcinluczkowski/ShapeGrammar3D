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
    public class SG_AutoRule061_3D : SG_Rule
    {

        // --- properties ---
        public string ElemName { get; set; }
        public int[] Domain { get; set; }

        // --- constructors ---
        public SG_AutoRule061_3D()
        {
        }

        //public SG_AutoRule060_3D(string _eName, int[] _domain)
        public SG_AutoRule061_3D(string _eName)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule060-3D";
            ElemName = _eName;
            // Domain = _domain;

            RuleMarker = UT.RULE061_MARKER;

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
            SH_CrossSection_Rectangle def_crosec = new SH_CrossSection_Rectangle(10, 10);
            def_crosec.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
            // find relevant range in genotype
            int sid = -999;
            int eid = -999;
            List<int> selectedIntGenes;
            List<double> selectedDGenes;

            gt.FindRange(ref sid, ref eid, UT.RULE061_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule061-3D - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            // double range = Domain[1] - Domain[0];

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
            if (studElements != null || studElements.Count() != 0)
            {
                initialElems = ss_ref.Elems.Where(e => e.Autorule == UT.RULE010_MARKER).ToList();
            }

            for (int i = 0; i < selectedIntGenes.Count; i++)
            {
                var stud0 = (SG_Elem1D)studElements[i];

                var iniCrv = ((SG_Elem1D)stud0.Nodes[0].Elements.Where(e => e.Autorule == UT.RULE010_MARKER).ToList()[0]).Init_Crv;

                var targetElements = new List<SG_Element>();
                for (int j = 0; j < studElements.Count; j++)
                {
                    var stud1 = (SG_Elem1D)studElements[j];
                    var iniCrv1 = ((SG_Elem1D)stud1.Nodes[0].Elements.Where(e => e.Autorule == UT.RULE010_MARKER).ToList()[0]).Init_Crv;

                    if (iniCrv.PointAtStart.CompareTo(iniCrv1.PointAtStart) != 0) // &&
                                                                                  //iniCrv.PointAtEnd.CompareTo(iniCrv1.PointAtEnd) == -1 &&
                                                                                  //iniCrv.GetLength() != iniCrv1.GetLength())
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

                var targetStud = targetElements.OrderBy(t => t.Nodes[0].Pt.DistanceTo(stud0.Nodes[0].Pt)).ToList()[0];

                var newBeam = new SG_Elem1D(new Line(stud0.Nodes[0].Pt, targetStud.Nodes[0].Pt), -999, "3DAR5", def_crosec) { Autorule = UT.RULE061_MARKER };

                ss_ref.AddNewElement(newBeam);


            }

            return "Auto-rule 061-3D successfully applied.";

        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }


    }
}
