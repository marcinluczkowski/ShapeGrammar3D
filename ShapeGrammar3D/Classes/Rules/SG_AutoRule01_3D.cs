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
        public double MaxSegmentLength { get; set; }
        private readonly double[] bounds = { 0.2, 0.8 };

        // --- constructors --- 
        public SG_AutoRule01_3D()
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_01_3D";
            RuleMarker = UT.RULE010_MARKER;
        }

        public SG_AutoRule01_3D(List<string> _eNames, double maxSegLen = 0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_01_3D";
            ElemNames = _eNames;
            MaxSegmentLength = maxSegLen;
            RuleMarker = UT.RULE010_MARKER;
        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Elements;

        public override int GetChromosomeLength(SG_Shape shape)
        {
            if (MaxSegmentLength > 0)
                return 2;
            return base.GetChromosomeLength(shape);
        }

        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE010_MARKER);
            if (sid == -999 || eid == -999)
                return "Autorule010_3D - wrong marker";

            if (MaxSegmentLength > 0)
                return DeterministicSubdivision(ref ss_ref);

            return GeneBasedSubdivision(ref ss_ref, ref gt, sid, eid);
        }

        /// <summary>
        /// Deterministic mode: subdivide every beam until all segments ≤ MaxSegmentLength.
        /// </summary>
        private string DeterministicSubdivision(ref SG_Shape ss_ref)
        {
            int totalNewNodes = 0;
            var removeIds = new List<int>();
            var newElems = new List<SG_Element>();

            foreach (var e in new List<SG_Element>(ss_ref.Elems))
            {
                var elem = e as SG_Elem1D;
                if (elem?.Crv == null) continue;

                double len = elem.Crv.GetLength();
                if (len <= MaxSegmentLength || len < UT.MIN_SEG_LEN * 2)
                    continue;

                int numSeg = (int)Math.Ceiling(len / MaxSegmentLength);
                if (numSeg < 2) continue;

                Interval domain = elem.Crv.Domain;

                var segNodes = new List<SG_Node> { elem.Nodes[0] };
                for (int k = 1; k < numSeg; k++)
                {
                    double t = (double)k / numSeg;
                    SG_Node nd = SG_Node.CreateNodeOnCrv(elem, t, ss_ref.nodeCount);
                    ss_ref.Nodes.Add(nd);
                    ss_ref.nodeCount++;
                    segNodes.Add(nd);
                    totalNewNodes++;
                }
                segNodes.Add(elem.Nodes[1]);

                for (int k = 0; k < numSeg; k++)
                {
                    double t0 = domain.ParameterAt((double)k / numSeg);
                    double t1 = domain.ParameterAt((double)(k + 1) / numSeg);

                    Curve subCrv = elem.Crv.Trim(t0, t1);
                    if (subCrv == null)
                    {
                        Line subLn = new Line(segNodes[k].Pt, segNodes[k + 1].Pt);
                        subCrv = subLn.ToNurbsCurve();
                    }

                    var ne = new SG_Elem1D(
                        new SG_Node[] { segNodes[k], segNodes[k + 1] },
                        subCrv, elem.Init_Crv,
                        ss_ref.elementCount, elem.Name, elem.CrossSection
                    ) { Autorule = UT.RULE010_MARKER };
                    ne.Joined_Init_Crv = elem.Joined_Init_Crv;

                    newElems.Add(ne);
                    ss_ref.elementCount++;
                }

                removeIds.Add(elem.ID);
            }

            ss_ref.Elems.AddRange(newElems);
            if (removeIds.Count > 0)
                ss_ref.Elems = ss_ref.Elems.Where(e => !removeIds.Contains(e.ID)).ToList();

            return $"Auto-rule 01_3D: {totalNewNodes} nodes added (maxSeg={MaxSegmentLength:F1}), {ss_ref.Elems.Count} elems total";
        }

        /// <summary>
        /// Original GA gene-based mode: each gene controls one element split.
        /// </summary>
        private string GeneBasedSubdivision(ref SG_Shape ss_ref, ref SG_Genotype gt, int sid, int eid)
        {
            var selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            var selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            List<int> removeIds = new List<int>();
            for (int i = 0; i < selectedIntGenes.Count; i++)
            {
                if (selectedIntGenes[i] == 0) continue;
                if (i >= ss_ref.Elems.Count) break;

                SG_Elem1D elem = ss_ref.Elems[i] as SG_Elem1D;
                if (elem?.Crv == null) continue;

                double param = selectedDGenes[i];
                if (param < bounds[0]) param = bounds[0];
                else if (param > bounds[1]) param = bounds[1];

                Interval i1 = elem.Crv.Domain;

                double seglen1 = elem.Crv.GetLength(new Interval(i1.Min, i1.ParameterAt(param)));
                double seglen2 = elem.Crv.GetLength(new Interval(i1.ParameterAt(param), i1.Max));

                if (seglen1 < UT.MIN_SEG_LEN || seglen2 < UT.MIN_SEG_LEN)
                {
                    ss_ref.Elems = ss_ref.Elems.Where(e => removeIds.Contains(e.ID) == false).ToList();
                    return "Segments are too short for Autorule01_3D.";
                }

                SG_Node midNode = SG_Node.CreateNodeOnCrv(elem, param, ss_ref.nodeCount);
                ss_ref.Nodes.Add(midNode);
                ss_ref.nodeCount++;

                SG_Elem1D newElm0 = new SG_Elem1D(new SG_Node[2] { elem.Nodes[0], midNode }, elem.Crv.Split(i1.ParameterAt(param))[0], elem.Init_Crv, ss_ref.elementCount, elem.Name, elem.CrossSection) { Autorule = UT.RULE010_MARKER };
                SG_Elem1D newElm1 = new SG_Elem1D(new SG_Node[] { midNode, elem.Nodes[1] }, elem.Crv.Split(i1.ParameterAt(param))[1], elem.Init_Crv, ss_ref.elementCount + 1, elem.Name, elem.CrossSection) { Autorule = UT.RULE010_MARKER };

                newElm0.Joined_Init_Crv = elem.Joined_Init_Crv;
                newElm1.Joined_Init_Crv = elem.Joined_Init_Crv;

                ss_ref.elementCount += 2;

                removeIds.Add(elem.ID);
                ss_ref.Elems.AddRange(new List<SG_Element>() { newElm0, newElm1 });
            }

            ss_ref.Elems = ss_ref.Elems.Where(e => removeIds.Contains(e.ID) == false).ToList();

            return "Auto-rule 01_3D successfully applied.";
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
    }
}
