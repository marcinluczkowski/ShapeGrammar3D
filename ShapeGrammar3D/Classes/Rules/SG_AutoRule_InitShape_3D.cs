using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// AutoRule that generates an initial beam structure from boundary lines.
    ///
    /// Two kinds of boundary line:
    ///   - Required: guaranteed to have ≥ MinSupportsPerLine active support points.
    ///     The first two required lines are treated as paired sides: after GA placement,
    ///     support counts are equalised and <see cref="MinSupportSpacing"/> is enforced
    ///     along every required line.
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
        /// <summary>Minimum chord distance between consecutive support points on each required line (model units). 0 disables spacing enforcement except near-duplicate merge.</summary>
        public double MinSupportSpacing { get; set; }
        /// <summary>
        /// When true and a Boundary (Brep or Mesh) is supplied, each candidate init beam is
        /// generated as a curve obtained by projecting the straight chord between its two
        /// support points vertically (world +Z) onto the boundary's upper surface — i.e.
        /// the beam follows the gable / roof profile instead of being a straight chord.
        /// When false (default), beams are straight lines as before.
        /// </summary>
        public bool ProjectBeamsToRoof { get; set; }
        /// <summary>Number of samples taken along each chord when projecting to the roof.</summary>
        public int ProjectionSampleCount { get; set; } = 21;

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
            int boundaryBeamConstraintMode = 0,
            double minSupportSpacing = 0.0,
            bool projectBeamsToRoof = false,
            int projectionSampleCount = 21)
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
            MinSupportSpacing = Math.Max(0.0, minSupportSpacing);
            ProjectBeamsToRoof = projectBeamsToRoof;
            ProjectionSampleCount = Math.Max(3, projectionSampleCount);
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
                src.BoundaryBrep, src.BoundaryMesh, src.BoundaryBeamConstraintMode,
                src.MinSupportSpacing,
                src.ProjectBeamsToRoof,
                src.ProjectionSampleCount);
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

            // ---- Required lines: min spacing along line, then equal counts on first two sides ----
            for (int li = 0; li < reqCount; li++)
            {
                var ln = allLines[li];
                EnforceMinChordSpacingOnLine(ln, pointsByLine[li], MinSupportSpacing);
                pointsByLine[li] = pointsByLine[li]
                    .Where(lp => IsInsideBoundary(lp.pt))
                    .ToList();
            }

            if (reqCount >= 2)
                BalanceFirstTwoRequiredSides(allLines, pointsByLine, reqCount);

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
            int curvedBeamCount = 0;
            int checkedBeamCount = 0;
            int outsideBeamCount = 0;
            bool projectionEnabled = ProjectBeamsToRoof && (BoundaryBrep != null || BoundaryMesh != null);
            BoundingBox roofBb = projectionEnabled
                ? (BoundaryBrep != null ? BoundaryBrep.GetBoundingBox(true) : BoundaryMesh.GetBoundingBox(true))
                : BoundingBox.Empty;
            double roofZStart = projectionEnabled && roofBb.IsValid
                ? roofBb.Max.Z + Math.Max(1.0, roofBb.Diagonal.Length * 0.01)
                : 0.0;

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

                        Curve roofCrv = null;
                        if (projectionEnabled)
                            roofCrv = ProjectChordToRoof(ln, roofZStart, ProjectionSampleCount);

                        SG_Elem1D beam;
                        if (roofCrv != null)
                        {
                            beam = new SG_Elem1D(roofCrv, -999, "beam", crosec)
                            {
                                CrossSection = crosec
                            };
                            curvedBeamCount++;
                        }
                        else
                        {
                            beam = new SG_Elem1D(ln, -999, "beam", crosec);
                        }
                        beam.Joined_Init_Crv = beam.Init_Crv?.DuplicateCurve();
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

            if (AreaLoadVector.Length > 1e-12)
            {
                var loadNodes = VoronoiAreaLoadUtil.CollectAreaLoadVoronoiSeedNodes(ss, DesignSpace);

                if (loadNodes.Count > 0)
                {
                    var seeds = loadNodes.Select(n => (n.ID, n.Pt.X, n.Pt.Y)).ToList();
                    var areas = ComputeVoronoiAreas(
                        seeds,
                        DesignSpace.Min.X, DesignSpace.Max.X,
                        DesignSpace.Min.Y, DesignSpace.Max.Y);

                    foreach (var n in loadNodes)
                    {
                        if (!areas.TryGetValue(n.ID, out double area)) continue;
                        ss.PointLoads.Add(new SG_PointLoad(area * AreaLoadVector, Vector3d.Zero, n.Pt));
                    }
                }
            }
            else
            {
                foreach (var nd in ss.Nodes)
                    ss.PointLoads.Add(new SG_PointLoad(LoadVector, Vector3d.Zero, nd.Pt));
            }

            ss.SimpleShapeState = State.alpha;
            ss.BoundaryViolationRatio = checkedBeamCount > 0
                ? (double)outsideBeamCount / checkedBeamCount
                : 0.0;
            ss.BoundaryViolationWeight = BoundaryBeamConstraintMode > 1 ? BoundaryBeamConstraintMode : 0.0;

            int totalPts = pointsByLine.Sum(pl => pl.Count);
            string projTag = projectionEnabled ? $" (curved={curvedBeamCount})" : "";
            return $"InitShape: {totalPts} pts on {allLines.Count} lines, {beamCount} beams{projTag}, {ss.Supports.Count} supports, {ss.Nodes.Count} nodes";
        }

        /// <summary>
        /// Builds a curve following the roof surface above the chord <paramref name="chord"/>
        /// by sampling along the chord and casting a vertical ray downward from above the
        /// boundary's bounding box for each sample. The chord endpoints (support points on
        /// the eaves) are kept exactly as-is, so the resulting curve is C0-continuous with
        /// the supports. Returns <c>null</c> when projection cannot recover at least 3
        /// useful points (in which case the caller falls back to the straight chord).
        /// </summary>
        private Curve ProjectChordToRoof(Line chord, double zStart, int samples)
        {
            if (BoundaryBrep == null && BoundaryMesh == null) return null;
            if (samples < 3) samples = 3;

            var pts = new List<Point3d>(samples + 1);
            var down = -Vector3d.ZAxis;
            int hits = 0;

            // Force the chord endpoints to remain exactly on the eaves so the beam
            // connects to the support point without any drift.
            for (int i = 0; i <= samples; i++)
            {
                double t = (double)i / samples;
                Point3d basePt = chord.PointAt(t);
                Point3d sample = basePt;
                bool isEnd = (i == 0 || i == samples);

                if (!isEnd)
                {
                    Point3d origin = new Point3d(basePt.X, basePt.Y, zStart);
                    Ray3d ray = new Ray3d(origin, down);
                    if (TryRayShootBoundary(ray, out Point3d hit))
                    {
                        // Only adopt the hit if it sits at or above the chord level —
                        // otherwise the projection has fallen onto an underside / floor
                        // face and would yield a degenerate beam.
                        if (hit.Z + 1e-9 >= basePt.Z)
                        {
                            sample = hit;
                            hits++;
                        }
                    }
                }
                pts.Add(sample);
            }

            // Need at least one interior projected sample, otherwise the curve
            // would just reproduce the straight chord.
            if (hits < 1) return null;

            // Drop near-duplicate consecutive samples (rays sometimes produce slight
            // coincidence at flat parts of the roof) so InterpolatedCurve stays valid.
            var cleaned = new List<Point3d> { pts[0] };
            for (int k = 1; k < pts.Count; k++)
            {
                if (pts[k].DistanceToSquared(cleaned[cleaned.Count - 1]) > 1e-12)
                    cleaned.Add(pts[k]);
            }
            if (cleaned.Count < 3) return null;

            // Use a degree-1 NURBS (i.e. polyline-like) interpolant so a sharp ridge
            // is reproduced exactly. Subsequent rules use Curve.Split / FrameAt which
            // both work on degree-1 NURBS.
            return Curve.CreateInterpolatedCurve(cleaned, 1);
        }

        /// <summary>Cast <paramref name="ray"/> against the boundary Brep first, then the
        /// boundary Mesh. Returns the first intersection point if any.</summary>
        private bool TryRayShootBoundary(Ray3d ray, out Point3d hit)
        {
            hit = Point3d.Unset;
            if (BoundaryBrep != null)
            {
                try
                {
                    var hits = Intersection.RayShoot(ray, new[] { (GeometryBase)BoundaryBrep }, 1);
                    if (hits != null && hits.Length > 0 && hits[0].IsValid)
                    {
                        hit = hits[0];
                        return true;
                    }
                }
                catch { }
            }
            if (BoundaryMesh != null)
            {
                try
                {
                    double tHit = Intersection.MeshRay(BoundaryMesh, ray);
                    if (tHit >= 0)
                    {
                        hit = ray.Position + ray.Direction * tHit;
                        return true;
                    }
                }
                catch { }
            }
            return false;
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
            => BoundaryConstraintUtil.IsPointInside(pt, BoundaryBrep, BoundaryMesh);

        private bool IsLineOutsideBoundary(Line ln)
            => BoundaryConstraintUtil.IsLineOutsideBoundary(ln, BoundaryBrep, BoundaryMesh);

        /// <summary>
        /// Upper bound on support count along a segment of length <paramref name="lineLength"/>
        /// when consecutive supports must be at least <paramref name="minSpacing"/> apart.
        /// </summary>
        private static int MaxSupportPointCountForMinSpacing(double lineLength, double minSpacing)
        {
            if (minSpacing <= 1e-12 || lineLength <= 1e-12)
                return int.MaxValue / 8;
            return Math.Max(2, (int)Math.Floor(lineLength / minSpacing) + 1);
        }

        /// <summary>
        /// Drops interior points that are closer than <paramref name="minSpacing"/> to the
        /// previous kept point; first and last points in parameter order are always kept.
        /// When <paramref name="minSpacing"/> ≤ 0, only merges near-duplicate points (&lt;1e-9).
        /// </summary>
        private static void EnforceMinChordSpacingOnLine(Line line, List<(double t, Point3d pt)> pts, double minSpacing)
        {
            if (pts == null || pts.Count <= 1) return;

            double tol = minSpacing > 1e-12 ? minSpacing : 1e-9;
            pts.Sort((a, b) => a.t.CompareTo(b.t));

            var kept = new List<(double t, Point3d pt)> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var p = pts[i];
                if (p.pt.DistanceTo(kept[kept.Count - 1].pt) >= tol - 1e-12)
                    kept.Add(p);
            }

            var last = pts[pts.Count - 1];
            while (kept.Count > 1 && last.pt.DistanceTo(kept[kept.Count - 1].pt) < tol - 1e-12)
                kept.RemoveAt(kept.Count - 1);
            kept.Add(last);

            pts.Clear();
            pts.AddRange(kept);
        }

        /// <summary>
        /// Ensures required lines 0 and 1 carry the same number of support points and
        /// respects <see cref="MinSupportsPerLine"/> and <see cref="MinSupportSpacing"/> caps.
        /// </summary>
        private void BalanceFirstTwoRequiredSides(
            List<Line> allLines,
            List<List<(double t, Point3d pt)>> pointsByLine,
            int reqCount)
        {
            if (reqCount < 2) return;

            var line0 = allLines[0];
            var line1 = allLines[1];
            var pts0 = pointsByLine[0];
            var pts1 = pointsByLine[1];
            if (pts0.Count == 0 || pts1.Count == 0) return;

            int maxFeas = Math.Min(
                MaxSupportPointCountForMinSpacing(line0.Length, MinSupportSpacing),
                MaxSupportPointCountForMinSpacing(line1.Length, MinSupportSpacing));

            int target = Math.Max(Math.Max(pts0.Count, pts1.Count), MinSupportsPerLine);
            if (maxFeas < int.MaxValue / 8)
                target = Math.Min(target, maxFeas);

            target = Math.Max(2, target);

            AdjustPointListToCount(line0, pts0, target, MinSupportSpacing);
            AdjustPointListToCount(line1, pts1, target, MinSupportSpacing);

            int n = Math.Min(pts0.Count, pts1.Count);
            TrimToCount(pts0, n, MinSupportSpacing);
            TrimToCount(pts1, n, MinSupportSpacing);

            pts0 = pts0.Where(lp => IsInsideBoundary(lp.pt)).ToList();
            pts1 = pts1.Where(lp => IsInsideBoundary(lp.pt)).ToList();
            pointsByLine[0] = pts0;
            pointsByLine[1] = pts1;

            n = Math.Min(pts0.Count, pts1.Count);
            TrimToCount(pts0, n, MinSupportSpacing);
            TrimToCount(pts1, n, MinSupportSpacing);
        }

        private static void TrimToCount(List<(double t, Point3d pt)> pts, int n, double minSpacing)
        {
            while (pts.Count > n)
            {
                if (pts.Count <= 2) break;
                int before = pts.Count;
                RemoveWeakestInteriorPoint(pts, minSpacing);
                if (pts.Count == before) break;
            }
        }

        private void AdjustPointListToCount(Line line, List<(double t, Point3d pt)> pts, int target, double minSpacing)
        {
            if (target < 2) target = 2;
            pts.Sort((a, b) => a.t.CompareTo(b.t));

            while (pts.Count > target)
                RemoveWeakestInteriorPoint(pts, minSpacing);

            int guard = 0;
            while (pts.Count < target && guard++ < target * 4)
            {
                if (!TryAddMidGapPoint(line, pts, minSpacing))
                    break;
            }

            pts.Sort((a, b) => a.t.CompareTo(b.t));
        }

        private bool TryAddMidGapPoint(Line line, List<(double t, Point3d pt)> pts, double minSpacing)
        {
            if (pts.Count < 1) return false;
            pts.Sort((a, b) => a.t.CompareTo(b.t));

            double bestGap = -1.0;
            int bestI = -1;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double g = pts[i + 1].pt.DistanceTo(pts[i].pt);
                if (g > bestGap)
                {
                    bestGap = g;
                    bestI = i;
                }
            }

            if (bestI < 0 || bestGap < 1e-12) return false;
            double tol = minSpacing > 1e-12 ? minSpacing : 1e-9;
            if (bestGap < tol * 2.0 - 1e-12) return false;

            double tMid = 0.5 * (pts[bestI].t + pts[bestI + 1].t);
            var pMid = line.PointAt(tMid);
            if (!IsInsideBoundary(pMid)) return false;
            if (pMid.DistanceTo(pts[bestI].pt) < tol - 1e-12 || pMid.DistanceTo(pts[bestI + 1].pt) < tol - 1e-12)
                return false;

            pts.Add((tMid, pMid));
            return true;
        }

        private static void RemoveWeakestInteriorPoint(List<(double t, Point3d pt)> pts, double minSpacing)
        {
            if (pts.Count <= 2) return;

            pts.Sort((a, b) => a.t.CompareTo(b.t));
            int bestIdx = -1;
            double bestMargin = double.MaxValue;

            for (int i = 1; i < pts.Count - 1; i++)
            {
                double dL = pts[i].pt.DistanceTo(pts[i - 1].pt);
                double dR = pts[i].pt.DistanceTo(pts[i + 1].pt);
                double margin = Math.Min(dL, dR);
                if (margin < bestMargin)
                {
                    bestMargin = margin;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
                pts.RemoveAt(bestIdx);
        }

        private static Dictionary<int, double> ComputeVoronoiAreas(
            List<(int nodeId, double x, double y)> tips,
            double xMin, double xMax, double yMin, double yMax,
            int gridRes = 100)
        {
            double totalW = xMax - xMin;
            double totalH = yMax - yMin;
            if (tips == null || tips.Count == 0)
                return new Dictionary<int, double>();
            if (totalW < 1e-9 || totalH < 1e-9)
                return tips.ToDictionary(t => t.nodeId, _ => 0.0);

            double totalArea = totalW * totalH;
            double cellArea = totalArea / (gridRes * gridRes);

            var counts = new Dictionary<int, int>();
            foreach (var t in tips) counts[t.nodeId] = 0;

            double dx = totalW / gridRes;
            double dy = totalH / gridRes;

            for (int iy = 0; iy < gridRes; iy++)
            {
                double gy = yMin + (iy + 0.5) * dy;
                for (int ix = 0; ix < gridRes; ix++)
                {
                    double gx = xMin + (ix + 0.5) * dx;
                    double bestDistSq = double.MaxValue;
                    int bestId = tips[0].nodeId;
                    foreach (var t in tips)
                    {
                        double ddx = gx - t.x;
                        double ddy = gy - t.y;
                        double dSq = ddx * ddx + ddy * ddy;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestId = t.nodeId;
                        }
                    }
                    counts[bestId]++;
                }
            }

            return counts.ToDictionary(kv => kv.Key, kv => kv.Value * cellArea);
        }
    }
}
