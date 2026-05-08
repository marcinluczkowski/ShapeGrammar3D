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

        /// <summary>
        /// Maximum allowed segment length. Together with <see cref="MinSegmentLength"/>
        /// this defines the *range* of allowed division counts per beam:
        ///   numSegMin = ceil(len / MaxSegmentLength)   (guaranteed minimum subdivisions)
        ///   numSegMax = floor(len / MinSegmentLength)  (allowed upper bound)
        /// 0 = use legacy gene-based single-split mode.
        /// </summary>
        public double MaxSegmentLength { get; set; }

        /// <summary>
        /// Minimum allowed segment length. When &gt; 0 and &lt; MaxSegmentLength, the GA
        /// gene picks the *number* of subdivisions in [numSegMin, numSegMax] for each beam,
        /// giving real variety in division counts across the population. When 0 (default),
        /// every individual gets exactly numSegMin subdivisions and only the gene-driven
        /// phase offset varies node positions.
        /// </summary>
        public double MinSegmentLength { get; set; } = 0.0;

        private readonly double[] bounds = { 0.2, 0.8 };

        /// <summary>
        /// When true and MaxSegmentLength > 0, interior node positions are shifted
        /// by a per-element gene-driven phase offset instead of being perfectly uniform.
        /// This gives the GA variety in node placement while still guaranteeing the
        /// minimum number of segments per element.
        /// </summary>
        public bool RandomizePositions { get; set; } = true;

        // --- constructors --- 
        public SG_AutoRule01_3D()
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_01_3D";
            RuleMarker = UT.RULE010_MARKER;
        }

        public SG_AutoRule01_3D(List<string> _eNames, double maxSegLen = 0, bool randomizePositions = true, double minSegLen = 0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_01_3D";
            ElemNames = _eNames;
            MaxSegmentLength = maxSegLen;
            MinSegmentLength = minSegLen;
            RandomizePositions = randomizePositions;
            RuleMarker = UT.RULE010_MARKER;
        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Elements;

        public override int GetChromosomeLength(SG_Shape shape)
        {
            // MaxSegmentLength mode allocates 2 genes per element so the GA can independently control:
            //   gene[2i]   → number of subdivisions  (used only when MinSegmentLength range is active)
            //   gene[2i+1] → phase offset            (used only when RandomizePositions is true)
            // Gene-based mode keeps 1 gene per element (split position).
            if (MaxSegmentLength > 0)
            {
                int elements = shape?.Elems?.Count ?? 0;
                return Math.Max(11, 2 * elements + 2);
            }
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
                return MaxSegSubdivision(ref ss_ref, ref gt, sid, eid);

            return GeneBasedSubdivision(ref ss_ref, ref gt, sid, eid);
        }

        /// <summary>
        /// MaxSegmentLength mode. For every beam:
        ///   numSegMin = ceil(len / MaxSegmentLength)              (lower bound — always guaranteed)
        ///   numSegMax = floor(len / MinSegmentLength) when set    (upper bound)
        /// Two genes per element drive variety:
        ///   gene[2i]   → numSeg in [numSegMin, numSegMax]
        ///   gene[2i+1] → phase offset that shifts interior nodes away from the uniform grid
        /// First-generation random doubles therefore spread division counts across the full range,
        /// not just the minimum.
        /// </summary>
        private string MaxSegSubdivision(ref SG_Shape ss_ref, ref SG_Genotype gt, int sid, int eid)
        {
            var selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            int totalNewNodes = 0;
            var removeIds = new List<int>();
            var newElems  = new List<SG_Element>();

            var elemList = new List<SG_Element>(ss_ref.Elems);
            for (int i = 0; i < elemList.Count; i++)
            {
                var elem = elemList[i] as SG_Elem1D;
                if (elem?.Crv == null) continue;

                double len = elem.Crv.GetLength();
                if (len <= MaxSegmentLength || len < UT.MIN_SEG_LEN * 2) continue;

                // --- 1. Pick number of segments in [numSegMin, numSegMax] ---
                int numSegMin = Math.Max(2, (int)Math.Ceiling(len / MaxSegmentLength));
                int numSegMax = numSegMin;
                if (MinSegmentLength > 0 && MinSegmentLength < MaxSegmentLength)
                {
                    int candidateMax = (int)Math.Floor(len / Math.Max(MinSegmentLength, UT.MIN_SEG_LEN));
                    numSegMax = Math.Max(numSegMin, candidateMax);
                }

                int countGeneIdx = 2 * i;
                int phaseGeneIdx = 2 * i + 1;

                int numSeg = numSegMin;
                if (numSegMax > numSegMin && countGeneIdx < selectedDGenes.Count)
                {
                    double gCount = selectedDGenes[countGeneIdx]; // [0, 1]
                    // Uniform integer pick — guarantees the first generation spans coarse → fine.
                    numSeg = numSegMin + (int)Math.Floor(gCount * (numSegMax - numSegMin + 1));
                    if (numSeg > numSegMax) numSeg = numSegMax;
                    if (numSeg < numSegMin) numSeg = numSegMin;
                }
                if (numSeg < 2) continue;

                // --- 2. Pick phase offset (shifts uniform grid for natural-looking irregularity) ---
                double phase = 0.0;
                if (RandomizePositions && phaseGeneIdx < selectedDGenes.Count)
                {
                    double g = selectedDGenes[phaseGeneIdx]; // [0, 1]
                    double maxPhase = Math.Max(0.0, 1.0 - UT.MIN_SEG_LEN * numSeg / len);
                    maxPhase = Math.Min(0.5, maxPhase);
                    phase = (g - 0.5) * 2.0 * maxPhase;
                }

                Interval domain = elem.Crv.Domain;

                // Normalised interior node positions: t_k = (k + phase) / numSeg, k = 1..numSeg-1
                var segNodes = new List<SG_Node> { elem.Nodes[0] };
                for (int k = 1; k < numSeg; k++)
                {
                    double t = Math.Clamp((k + phase) / numSeg, UT.MIN_SEG_LEN / len, 1.0 - UT.MIN_SEG_LEN / len);
                    SG_Node nd = SG_Node.CreateNodeOnCrv(elem, t, ss_ref.nodeCount);
                    ss_ref.Nodes.Add(nd);
                    ss_ref.nodeCount++;
                    segNodes.Add(nd);
                    totalNewNodes++;
                }
                segNodes.Add(elem.Nodes[1]);

                // Build sub-elements between consecutive nodes
                for (int k = 0; k < numSeg; k++)
                {
                    double tStart = k == 0
                        ? 0.0
                        : Math.Clamp((k + phase) / numSeg, UT.MIN_SEG_LEN / len, 1.0 - UT.MIN_SEG_LEN / len);
                    double tEnd = k == numSeg - 1
                        ? 1.0
                        : Math.Clamp((k + 1 + phase) / numSeg, UT.MIN_SEG_LEN / len, 1.0 - UT.MIN_SEG_LEN / len);

                    double p0 = domain.ParameterAt(tStart);
                    double p1 = domain.ParameterAt(tEnd);

                    Curve subCrv = elem.Crv.Trim(p0, p1);
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

            string posMode = RandomizePositions ? "phase-shift" : "uniform";
            string countMode = (MinSegmentLength > 0 && MinSegmentLength < MaxSegmentLength)
                ? $"count∈[{MinSegmentLength:F1}–{MaxSegmentLength:F1}m]"
                : $"maxSeg={MaxSegmentLength:F1}m";
            return $"Auto-rule 01_3D ({posMode}, {countMode}): {totalNewNodes} nodes added, {ss_ref.Elems.Count} elems total";
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
