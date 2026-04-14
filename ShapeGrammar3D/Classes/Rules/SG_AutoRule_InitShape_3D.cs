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
        public Brep BoundaryBrep { get; set; }
        public Mesh BoundaryMesh { get; set; }
        public SH_CrossSection_Beam CrossSection { get; set; }
        public Vector3d LoadVector { get; set; }
        public string SupportCondition { get; set; }
        public Vector3d AreaLoadVector { get; set; }
        public bool UseSelfWeight { get; set; }
        public int BoundaryBeamConstraintMode { get; set; }

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
            string supportCondition,
            Vector3d areaLoadVector,
            bool useSelfWeight = false,
            Brep boundaryBrep = null,
            Mesh boundaryMesh = null,
            int boundaryBeamConstraintMode = 0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_InitShape_3D";
            RuleMarker = UT.RULE_INITSHAPE_MARKER;
            DesignSpace = designSpace;
            BoundaryBrep = boundaryBrep?.DuplicateBrep();
            BoundaryMesh = boundaryMesh?.DuplicateMesh();
            RequiredLines = requiredLines ?? new List<Line>();
            OptionalLines = optionalLines ?? new List<Line>();
            MaxPointsPerLine = Math.Max(2, maxPointsPerLine);
            MinSupportsPerLine = Math.Max(2, minSupportsPerLine);
            CrossSection = crossSection;
            LoadVector = loadVector;
            SupportCondition = supportCondition;
            AreaLoadVector = areaLoadVector;
            UseSelfWeight = useSelfWeight;
            BoundaryBeamConstraintMode = boundaryBeamConstraintMode;
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
                src.CrossSection, src.LoadVector, src.SupportCondition,
                src.AreaLoadVector, src.UseSelfWeight,
                src.BoundaryBrep, src.BoundaryMesh, src.BoundaryBeamConstraintMode);
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

            // ---- Normalize line directions relative to the first line ----
            if (allLines.Count >= 2)
            {
                var refDir = allLines[0].Direction;
                for (int li = 1; li < allLines.Count; li++)
                {
                    if (refDir * allLines[li].Direction < -1e-6)
                        allLines[li] = new Line(allLines[li].To, allLines[li].From);
                }
            }

            // ---- Determine active support points per boundary line ----
            // Each point carries its parameter t on the line
            var pointsByLine = new List<List<(double t, Point3d pt)>>();

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

                var linePoints = new List<(double t, Point3d pt)>();

                // Required lines: always include both endpoints (t=0 and t=1)
                if (isRequired)
                    linePoints.Add((0.0, line.From));

                for (int k = 0; k < candidates.Count; k++)
                {
                    if (!activated[k]) continue;
                    double t = candidates[k].t;
                    if (isRequired && (t < 0.001 || t > 0.999)) continue;
                    linePoints.Add((t, line.PointAt(t)));
                }

                if (isRequired)
                    linePoints.Add((1.0, line.To));

                linePoints.Sort((a, b) => a.t.CompareTo(b.t));
                linePoints = linePoints
                    .Where(lp => IsInsideBoundary(lp.pt))
                    .ToList();

                // Fill deficit for required lines
                if (isRequired && linePoints.Count < MinSupportsPerLine)
                {
                    int deficit = MinSupportsPerLine - linePoints.Count;
                    for (int k = 0; k < deficit; k++)
                    {
                        double t = (k + 1.0) / (deficit + 1.0);
                        if (!linePoints.Any(lp => Math.Abs(lp.t - t) < 0.001))
                            linePoints.Add((t, line.PointAt(t)));
                    }
                    linePoints.Sort((a, b) => a.t.CompareTo(b.t));
                    linePoints = linePoints
                        .Where(lp => IsInsideBoundary(lp.pt))
                        .ToList();
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
            ss.BoundaryBrep = BoundaryBrep?.DuplicateBrep();
            ss.BoundaryMesh = BoundaryMesh?.DuplicateMesh();

            // ---- Create beams between DIFFERENT boundary lines ----
            int beamCount = 0;
            int checkedBeamCount = 0;
            int outsideBeamCount = 0;
            for (int i = 0; i < pointsByLine.Count; i++)
            {
                for (int j = i + 1; j < pointsByLine.Count; j++)
                {
                    if (pointsByLine[i].Count == 0 || pointsByLine[j].Count == 0)
                        continue;
                    foreach (var ln in TriangulateBetweenLines(pointsByLine[i], pointsByLine[j]))
                    {
                        if (!ln.IsValid || ln.Length < UT.PRES) continue;
                        checkedBeamCount++;
                        bool isOutside = IsLineOutsideBoundary(ln);
                        if (isOutside) outsideBeamCount++;
                        if (BoundaryBeamConstraintMode == 1 && isOutside) continue;
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
                foreach (var tp in pointsByLine[li])
                    supportPositions.Add(tp.pt);
            }

            foreach (var nd in ss.Nodes)
            {
                if (AreaLoadVector.Length <= 1e-12)
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
            ss.BoundaryViolationRatio = checkedBeamCount > 0
                ? (double)outsideBeamCount / checkedBeamCount
                : 0.0;
            ss.BoundaryViolationWeight = BoundaryBeamConstraintMode > 1 ? BoundaryBeamConstraintMode : 0.0;

            int totalPts = pointsByLine.Sum(pl => pl.Count);
            return $"InitShape: {totalPts} pts on {allLines.Count} lines, {beamCount} beams, {ss.Supports.Count} supports, {ss.Nodes.Count} nodes";
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Pairs points from two parameter-sorted sequences in order.
        /// A[0]→B[0], A[1]→B[1], ... producing exactly max(nA, nB) beams.
        /// If counts differ, remaining points connect to the last point on the shorter side.
        /// Lines are assumed to be pre-oriented consistently.
        /// </summary>
        private static List<Line> TriangulateBetweenLines(
            List<(double t, Point3d pt)> ptsA, List<(double t, Point3d pt)> ptsB)
        {
            var lines = new List<Line>();
            if (ptsA.Count == 0 || ptsB.Count == 0) return lines;

            var workB = new List<(double t, Point3d pt)>(ptsB);
            if (ptsA.Count >= 2 && workB.Count >= 2)
            {
                Vector3d dirA = ptsA[ptsA.Count - 1].pt - ptsA[0].pt;
                Vector3d dirB = workB[workB.Count - 1].pt - workB[0].pt;
                if (dirA * dirB < 0)
                {
                    workB.Reverse();
                    for (int k = 0; k < workB.Count; k++)
                        workB[k] = (1.0 - workB[k].t, workB[k].pt);
                }
            }

            int minCount = Math.Min(ptsA.Count, workB.Count);

            for (int i = 0; i < minCount; i++)
                lines.Add(new Line(ptsA[i].pt, workB[i].pt));

            if (ptsA.Count > workB.Count)
            {
                for (int i = minCount; i < ptsA.Count; i++)
                    lines.Add(new Line(ptsA[i].pt, workB[workB.Count - 1].pt));
            }
            else if (workB.Count > ptsA.Count)
            {
                for (int i = minCount; i < workB.Count; i++)
                    lines.Add(new Line(ptsA[ptsA.Count - 1].pt, workB[i].pt));
            }

            return lines;
        }

        private SH_CrossSection_Beam InheritCrossSection(SG_Shape ss)
        {
            var existing = ss.Elems?.OfType<SG_Elem1D>().FirstOrDefault()?.CrossSection;
            if (existing != null) return existing;
            var fb = new SH_CrossSection_Rectangle(10, 10);
            fb.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
            return fb;
        }

        private bool IsInsideBoundary(Point3d pt)
        {
            const double tol = 1e-6;

            if (BoundaryBrep != null)
                return BoundaryBrep.IsPointInside(pt, tol, false);

            if (BoundaryMesh != null)
                return BoundaryMesh.IsPointInside(pt, tol, false);

            return true;
        }

        private bool IsLineOutsideBoundary(Line ln)
        {
            var pts = new[]
            {
                ln.PointAt(0.25),
                ln.PointAt(0.5),
                ln.PointAt(0.75)
            };
            return pts.Any(p => !IsInsideBoundary(p));
        }
    }
}
