using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// AutoRule that generates an initial beam structure from boundary lines.
    ///
    /// Two kinds of boundary line:
    ///   - Required: guaranteed to have ≥ MinSupportsPerLine active support points.
    ///   - Optional: GA may place 0..MaxPointsPerLine points on them.
    ///
    /// Each candidate point consumes 1 IntGene (on/off) and 1 DGene (normalised
    /// position along the line, 0‥1).
    ///
    /// Beams are created only between points on DIFFERENT boundary lines using a
    /// greedy triangulation that produces a connected mesh of (nA+nB−1) beams
    /// per pair of lines.
    ///
    /// The BoundingBox is stored for future domain checking.
    /// </summary>
    [Serializable]
    public class SG_AutoRule_InitShape_3D : SG_Rule
    {
        public List<Line> RequiredLines { get; set; }
        public List<Line> OptionalLines { get; set; }
        public int MaxPointsPerLine { get; set; }
        public int MinSupportsPerLine { get; set; }
        public BoundingBox DesignSpace { get; set; }
        public SH_CrossSection_Beam CrossSection { get; set; }
        public Vector3d LoadVector { get; set; }
        public string SupportCondition { get; set; }

        /// <summary>Total gene slots – used by GrammarInterpreters for chromosome length estimation.</summary>
        public int MaxSupports => TotalLineCount * MaxPointsPerLine;

        private int TotalLineCount => (RequiredLines?.Count ?? 0) + (OptionalLines?.Count ?? 0);

        public SG_AutoRule_InitShape_3D() { }

        public SG_AutoRule_InitShape_3D(
            BoundingBox designSpace,
            List<Line> requiredLines,
            List<Line> optionalLines,
            int maxPointsPerLine,
            int minSupportsPerLine,
            SH_CrossSection_Beam crossSection,
            Vector3d loadVector,
            string supportCondition)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_InitShape_3D";
            RuleMarker = UT.RULE_INITSHAPE_MARKER;
            DesignSpace = designSpace;
            RequiredLines = requiredLines ?? new List<Line>();
            OptionalLines = optionalLines ?? new List<Line>();
            MaxPointsPerLine = Math.Max(2, maxPointsPerLine);
            MinSupportsPerLine = Math.Max(2, minSupportsPerLine);
            CrossSection = crossSection;
            LoadVector = loadVector;
            SupportCondition = supportCondition;
        }

        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Nodes;

        public override int GetChromosomeLength(SG_Shape shape)
        {
            int totalSlots = TotalLineCount * MaxPointsPerLine;
            return Math.Max(11, totalSlots + 2);
        }

        public override void NewRuleParameters(Random random, SG_Shape ss) { }

        public override SG_Rule CopyRule(SG_Rule rule)
        {
            var src = rule as SG_AutoRule_InitShape_3D;
            if (src == null) return this;
            return new SG_AutoRule_InitShape_3D(
                src.DesignSpace,
                src.RequiredLines != null ? new List<Line>(src.RequiredLines) : new List<Line>(),
                src.OptionalLines != null ? new List<Line>(src.OptionalLines) : new List<Line>(),
                src.MaxPointsPerLine,
                src.MinSupportsPerLine,
                src.CrossSection, src.LoadVector, src.SupportCondition);
        }

        public override State GetNextState() => State.beta;

        public override string RuleOperation(ref SG_Shape ss, ref SG_Genotype gt)
        {
            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE_INITSHAPE_MARKER);
            if (sid == -999 || eid == -999)
                return "SG_AutoRule_InitShape_3D - marker not found";

            var intGenes = gt.IntGenes.GetRange(sid, eid - sid);
            var dGenes   = gt.DGenes.GetRange(sid, eid - sid);

            var allLines = new List<Line>();
            int reqCount = RequiredLines?.Count ?? 0;
            if (RequiredLines != null) allLines.AddRange(RequiredLines);
            if (OptionalLines != null) allLines.AddRange(OptionalLines);

            if (allLines.Count < 2)
                return "InitShape: need at least 2 boundary lines";

            // ---- Determine active support points per boundary line ----
            var pointsByLine = new List<List<Point3d>>();

            for (int li = 0; li < allLines.Count; li++)
            {
                var line = allLines[li];
                bool isRequired = li < reqCount;
                int baseIdx = li * MaxPointsPerLine;

                var candidates = new List<(double t, bool active)>();
                for (int pi = 0; pi < MaxPointsPerLine; pi++)
                {
                    int idx = baseIdx + pi;
                    if (idx >= intGenes.Count || idx >= dGenes.Count) break;
                    double t = Math.Max(0, Math.Min(1, dGenes[idx]));
                    bool active = intGenes[idx] == 1;
                    candidates.Add((t, active));
                }

                candidates.Sort((a, b) => a.t.CompareTo(b.t));

                var activated = new bool[candidates.Count];
                int activeCount = 0;
                for (int k = 0; k < candidates.Count; k++)
                {
                    if (candidates[k].active)
                    {
                        activated[k] = true;
                        activeCount++;
                    }
                }

                if (isRequired)
                {
                    for (int k = 0; k < candidates.Count && activeCount < MinSupportsPerLine; k++)
                    {
                        if (!activated[k])
                        {
                            activated[k] = true;
                            activeCount++;
                        }
                    }
                }

                var linePoints = new List<Point3d>();
                for (int k = 0; k < candidates.Count; k++)
                {
                    if (activated[k])
                        linePoints.Add(line.PointAt(candidates[k].t));
                }

                if (isRequired && linePoints.Count < MinSupportsPerLine)
                {
                    int deficit = MinSupportsPerLine - linePoints.Count;
                    for (int k = 0; k < deficit; k++)
                    {
                        double t = (k + 1.0) / (deficit + 1.0);
                        linePoints.Add(line.PointAt(t));
                    }
                    linePoints = SortByLine(linePoints, line);
                }

                pointsByLine.Add(linePoints);
            }

            // ---- Clear shape ----
            var crosec = CrossSection ?? InheritCrossSection(ss);

            ss.Elems.Clear();
            ss.Nodes.Clear();
            ss.Supports.Clear();
            ss.PointLoads.Clear();
            ss.LineLoads ??= new List<SG_LineLoad>();
            ss.LineLoads.Clear();
            ss.elementCount = 0;
            ss.nodeCount = 0;

            // ---- Create beams between DIFFERENT boundary lines ----
            int beamCount = 0;
            for (int i = 0; i < pointsByLine.Count; i++)
            {
                for (int j = i + 1; j < pointsByLine.Count; j++)
                {
                    if (pointsByLine[i].Count == 0 || pointsByLine[j].Count == 0)
                        continue;
                    foreach (var ln in TriangulateBetweenLines(pointsByLine[i], pointsByLine[j]))
                    {
                        var beam = new SG_Elem1D(ln, -999, "beam", crosec);
                        beam.Joined_Init_Crv = beam.Init_Crv?.ToNurbsCurve();
                        ss.AddNewElement(beam);
                        beamCount++;
                    }
                }
            }

            // ---- Supports at ALL active points on required lines; loads at every node ----
            var supportPositions = new List<Point3d>();
            for (int li = 0; li < reqCount; li++)
            {
                foreach (var pt in pointsByLine[li])
                    supportPositions.Add(pt);
            }

            foreach (var nd in ss.Nodes)
            {
                ss.PointLoads.Add(new SG_PointLoad(LoadVector, Vector3d.Zero, nd.Pt));

                bool isSupport = supportPositions.Any(sp => nd.Pt.DistanceToSquared(sp) < 0.001);
                if (isSupport)
                {
                    var sup = new SG_Support(SupportCondition, nd.Pt);
                    sup.Node = nd;
                    nd.Support = sup;
                    ss.Supports.Add(sup);
                }
            }

            ss.RegisterElemsToNodes();
            ss.SimpleShapeState = State.alpha;

            int totalPts = pointsByLine.Sum(pl => pl.Count);
            return $"InitShape: {totalPts} pts on {allLines.Count} lines, {beamCount} beams, {ss.Supports.Count} supports, {ss.Nodes.Count} nodes";
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Greedy triangulation between two sorted point sequences.
        /// Produces a connected strip of (nA + nB − 1) beams.
        /// Handles opposing line orientations by checking dot product.
        /// </summary>
        private static List<Line> TriangulateBetweenLines(
            List<Point3d> ptsA, List<Point3d> ptsB)
        {
            var lines = new List<Line>();
            if (ptsA.Count == 0 || ptsB.Count == 0) return lines;

            var workB = new List<Point3d>(ptsB);
            if (ptsA.Count >= 2 && workB.Count >= 2)
            {
                Vector3d dirA = ptsA[ptsA.Count - 1] - ptsA[0];
                Vector3d dirB = workB[workB.Count - 1] - workB[0];
                if (dirA * dirB < 0)
                    workB.Reverse();
            }

            int i = 0, j = 0;
            lines.Add(new Line(ptsA[0], workB[0]));

            while (i < ptsA.Count - 1 || j < workB.Count - 1)
            {
                if (i >= ptsA.Count - 1)
                {
                    j++;
                    lines.Add(new Line(ptsA[i], workB[j]));
                }
                else if (j >= workB.Count - 1)
                {
                    i++;
                    lines.Add(new Line(ptsA[i], workB[j]));
                }
                else
                {
                    double d1 = ptsA[i + 1].DistanceToSquared(workB[j]);
                    double d2 = ptsA[i].DistanceToSquared(workB[j + 1]);
                    if (d1 <= d2)
                    {
                        i++;
                        lines.Add(new Line(ptsA[i], workB[j]));
                    }
                    else
                    {
                        j++;
                        lines.Add(new Line(ptsA[i], workB[j]));
                    }
                }
            }

            return lines;
        }

        /// <summary>Sort points by projection parameter along a reference line.</summary>
        private static List<Point3d> SortByLine(List<Point3d> pts, Line line)
        {
            Vector3d dir = line.To - line.From;
            double lenSq = dir.SquareLength;
            if (lenSq < 1e-12) return pts;
            return pts.OrderBy(p =>
            {
                Vector3d v = p - line.From;
                return (v.X * dir.X + v.Y * dir.Y + v.Z * dir.Z) / lenSq;
            }).ToList();
        }

        private SH_CrossSection_Beam InheritCrossSection(SG_Shape ss)
        {
            var existing = ss.Elems?.OfType<SG_Elem1D>().FirstOrDefault()?.CrossSection;
            if (existing != null) return existing;
            var fb = new SH_CrossSection_Rectangle(10, 10);
            fb.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
            return fb;
        }
    }
}
