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

        public SG_AutoRule061_3D(string _eName, int[] _domain)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule061-3D";
            ElemName = _eName;
            Domain = _domain;

            RuleMarker = UT.RULE061_MARKER;

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
            // algorithm for rule 061-3d
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

            gt.FindRange(ref sid, ref eid, UT.RULE061_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule061-3D - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            double range = Domain[1] - Domain[0]; 

            var studElements = ss_ref.Elems.Where(e => e.Name == "3DAR2").ToList();
            int geneCount = selectedIntGenes.Count;
            int addedCount = 0;

            for (int i = 0; i < studElements.Count; i++)
            {
                int geneIdx = i % geneCount;
                if (selectedIntGenes[geneIdx] == 0) continue;

                int numLns = (int) (selectedDGenes[geneIdx] * range);

                var stud0 = (SG_Elem1D)studElements[i];
                if (stud0.Nodes == null || stud0.Nodes.Length < 2 || stud0.Nodes[0] == null || stud0.Nodes[1] == null) continue;

                var iniElem061 = stud0.Nodes[0].Elements
                    .Where(e => e.Autorule == UT.RULE010_MARKER)
                    .OfType<SG_Elem1D>()
                    .FirstOrDefault();
                if (iniElem061 == null) continue;
                var joinedIniCrv = iniElem061.Joined_Init_Crv;
                if (joinedIniCrv == null) continue;

                var targetElements = new List<SG_Element>();
                for (int j = 0; j < studElements.Count; j++)
                {
                    if (i == j) continue;

                    var stud1 = (SG_Elem1D) studElements[j];
                    if (stud1.Nodes[0] == null) continue;

                    var iniElem1 = stud1.Nodes[0].Elements?
                        .Where(e => e.Autorule == UT.RULE010_MARKER)
                        .OfType<SG_Elem1D>()
                        .FirstOrDefault();
                    if (iniElem1 == null) continue;

                    var iniCrv1 = iniElem1.Joined_Init_Crv;
                    if (iniCrv1 == null) continue;

                    if (joinedIniCrv.PointAtStart.CompareTo(iniCrv1.PointAtStart) != 0) 
                    {
                        targetElements.Add(stud1);
                    }
                }

                var targetPts = new List<Point3d>();
                foreach (var elem in targetElements)
                {
                    var elem1D = (SG_Elem1D)elem;
                    foreach (var node in elem1D.Nodes)
                    {
                        targetPts.Add(node.Pt);
                    }
                }
                targetPts = targetPts.OrderBy(pt => pt.DistanceTo(stud0.Nodes[1].Pt)).ToList();
                if (targetPts.Count == 0) continue;

                int numBeams = Math.Min(numLns, targetPts.Count);
                for (int j = 0; j < numBeams; j++)
                {
                    var newLine = new Line(stud0.Nodes[1].Pt, targetPts[j]);
                    if (!newLine.IsValid || newLine.Length < UT.PRES) continue;
                    var new_beam = new SG_Elem1D(newLine, -999, "3DAR61", def_crosec) { Autorule = UT.RULE061_MARKER };

                    // Check if beam already exists at the same location
                    bool isDuplicate = ss_ref.Elems.OfType<SG_Elem1D>().Any(existingElem =>
                    {
                        if (existingElem.Nodes == null || existingElem.Nodes.Length < 2
                            || existingElem.Nodes[0] == null || existingElem.Nodes[1] == null)
                            return false;
                        var existingLine = new Line(existingElem.Nodes[0].Pt, existingElem.Nodes[1].Pt);
                        
                        bool sameDirection = newLine.From.DistanceTo(existingLine.From) < 0.001 && 
                                           newLine.To.DistanceTo(existingLine.To) < 0.001;
                        bool reverseDirection = newLine.From.DistanceTo(existingLine.To) < 0.001 && 
                                              newLine.To.DistanceTo(existingLine.From) < 0.001;
                        
                        return sameDirection || reverseDirection;
                    });

                    if (!isDuplicate)
                    {
                        ss_ref.AddNewElement(new_beam);
                        addedCount++;
                    }
                }
            }

            return $"Auto-rule 061-3D: {addedCount} cross-braces added from {studElements.Count} studs";

        }
        public override State GetNextState()
        {
            throw new NotImplementedException();
        }


    }
}
