using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Configurable thresholds and weights for feasibility penalties.
    /// Used so a simple beam gets relatively low (good) feasibility score.
    /// </summary>
    public struct FeasibilitySettings
    {
        public double WDang;
        public double WAng;
        public double WLen;
        public double WIntersect;
        public double WRepet;
        public double WDup;

        /// <summary>Angle [deg]: below this = full penalty.</summary>
        public double AngleMinDeg;
        /// <summary>Angle [deg]: gradient from full to zero between Min and Opt.</summary>
        public double AngleOptDeg;

        /// <summary>Length [m]: below = full penalty; 0.5–1.0 gradient to zero.</summary>
        public double LenTooShort;
        /// <summary>Length [m]: optimal range start (no penalty from here to LenOptHigh).</summary>
        public double LenOptLow;
        /// <summary>Length [m]: optimal range end.</summary>
        public double LenOptHigh;
        /// <summary>Length [m]: above this = gradient then full penalty.</summary>
        public double LenTooLong;

        public static FeasibilitySettings Default()
        {
            return new FeasibilitySettings
            {
                WDang = 0.20,
                WAng = 0.0,
                WLen = 0.0,
                WIntersect = 0.0,
                WRepet = 0.0,
                WDup = 0.0,
                AngleMinDeg = 10.0,
                AngleOptDeg = 30.0,
                LenTooShort = 0.5,
                LenOptLow = 1.0,
                LenOptHigh = 5.0,
                LenTooLong = 12.0
            };
        }
    }

    /// <summary>
    /// Computes feasibility metrics for an SG_Shape graph structure.
    /// Designed to run after chromosome decoding and RegisterElemsToNodes(),
    /// before expensive FEM analysis.  The penalty is gentle: it does not
    /// discard individuals, only increases the feasibility violation score.
    /// </summary>
    public static class FeasibilityMetrics
    {
        /// <summary>
        /// Computes all feasibility components using configurable settings.
        /// </summary>
        public static FeasibilityResult Compute(SG_Shape shape, FeasibilitySettings s)
        {
            var result = ComputeDanglingBarPenalty(shape);

            var angResult = ComputeAnglePenalty(shape, s.AngleMinDeg, s.AngleOptDeg);
            result.VAng = angResult.VAng;
            result.AngleViolationCount = angResult.AngleViolationCount;

            var lenResult = ComputeLengthPenalty(shape, s.LenTooShort, s.LenOptLow, s.LenOptHigh, s.LenTooLong);
            result.VLen = lenResult.VLen;
            result.LengthViolationCount = lenResult.LengthViolationCount;

            var intResult = ComputeIntersectionPenalty(shape);
            result.VIntersect = intResult.VIntersect;
            result.IntersectionCount = intResult.IntersectionCount;

            var repResult = ComputeRepetitivenessPenalty(shape, binTolerancePercent: 10.0);
            result.VRepet = repResult.VRepet;
            result.RepetitivenessBinCount = repResult.RepetitivenessBinCount;

            var dupResult = ComputeDuplicatePenalty(shape);
            result.VDup = dupResult.VDup;
            result.DuplicateCount = dupResult.DuplicateCount;
            result.VBoundary = ComputeBoundaryViolationRatio(shape);
            double wBoundary = Math.Max(0.0, shape?.BoundaryViolationWeight ?? 0.0);

            double total = 0.0;
            total += Math.Clamp(s.WDang * result.VDang, 0.0, 1.0);
            total += Math.Clamp(s.WAng * result.VAng, 0.0, 1.0);
            total += Math.Clamp(s.WLen * result.VLen, 0.0, 1.0);
            total += Math.Clamp(s.WIntersect * result.VIntersect, 0.0, 1.0);
            total += Math.Clamp(s.WRepet * result.VRepet, 0.0, 1.0);
            total += Math.Clamp(s.WDup * result.VDup, 0.0, 1.0);
            total += Math.Clamp(wBoundary * result.VBoundary, 0.0, 1.0);
            result.TotalViolation = Math.Clamp(total, 0.0, 1.0);

            return result;
        }

        /// <summary>
        /// Computes all feasibility components with simple weights (backward compatible).
        /// Uses default angle/length bands: angle &lt;10° full penalty, ≥30° zero; length 0.5–1–5–12 m.
        /// </summary>
        public static FeasibilityResult Compute(SG_Shape shape, double wDang = 0.20, double wAng = 0.0, double wLen = 0.0, double wIntersect = 0.0)
        {
            var s = FeasibilitySettings.Default();
            s.WDang = wDang;
            s.WAng = wAng;
            s.WLen = wLen;
            s.WIntersect = wIntersect;
            return Compute(shape, s);
        }

        /// <summary>
        /// Computes the dangling bar penalty from the graph topology.
        /// <para>
        /// A "dangling" edge has at least one endpoint node with degree ≤ 1.
        /// An "isolated" edge has both endpoints with degree ≤ 1
        /// (a bar connecting two nodes that connect to nothing else).
        /// </para>
        /// <para>
        /// Normalization (slightly stronger version):
        ///   vDang = (danglingCount + 0.5 * isolatedCount) / max(1, totalEdges)
        /// clamped to [0..1].
        /// </para>
        /// <para>
        /// High-degree nodes are never penalized; only bars that "hang"
        /// and do not participate in load-path / structure connectivity.
        /// </para>
        /// </summary>
        public static FeasibilityResult ComputeDanglingBarPenalty(SG_Shape shape)
        {
            var result = new FeasibilityResult();

            if (shape == null || shape.Elems == null || shape.Nodes == null
                || shape.Elems.Count == 0)
            {
                result.VDang = 0.0;
                return result;
            }



            int totalEdges = shape.Elems.Count;

            // Build node degree map.
            // Use dictionary to handle non-contiguous IDs safely.
            var degree = new Dictionary<int, int>(shape.Nodes.Count);
            foreach (var node in shape.Nodes)
            {
                degree[node.ID] = node.Elements.Count;
            }

            int danglingCount = 0;
            int isolatedCount = 0;

            foreach (var elem in shape.Elems)
            {
                // Guard against malformed elements
                if (elem.Nodes == null || elem.Nodes.Length < 2
                    || elem.Nodes[0] == null || elem.Nodes[1] == null)
                    continue;

                int idU = elem.Nodes[0].ID;
                int idV = elem.Nodes[1].ID;

                int du = degree.GetValueOrDefault(idU, 0);
                int dv = degree.GetValueOrDefault(idV, 0);

                bool uDangling = du <= 1;
                bool vDangling = dv <= 1;

                if (uDangling || vDangling)
                    danglingCount++;

                if (uDangling && vDangling)
                    isolatedCount++;
            }

            // Normalization with isolated-edge boost, clamped to [0..1]
            double raw = (danglingCount + 0.5 * isolatedCount) / Math.Max(1, totalEdges);
            result.VDang = Math.Clamp(raw, 0.0, 1.0);
            result.DanglingEdgeCount = danglingCount;
            result.IsolatedEdgeCount = isolatedCount;

            return result;
        }

        /// <summary>
        /// Computes angle-based penalty at all nodes (including upper nodes, not only baseline).
        /// For each node with 2+ incident elements, compares every element with every other element
        /// and uses the minimum angle. 0–minAngleDeg: full penalty; minAngleDeg–optAngleDeg: linear gradient; ≥optAngleDeg: no penalty.
        /// </summary>
        private static FeasibilityResult ComputeAnglePenalty(SG_Shape shape, double minAngleDeg = 10.0, double optAngleDeg = 30.0)
        {
            var res = new FeasibilityResult();

            if (shape == null || shape.Nodes == null || shape.Nodes.Count == 0)
            {
                res.VAng = 0.0;
                res.AngleViolationCount = 0;
                return res;
            }

            if (minAngleDeg >= optAngleDeg) optAngleDeg = minAngleDeg + 1.0;

            int violatingNodes = 0;
            double sumNodePenalties = 0.0;
            int evaluatedNodes = 0;

            foreach (var node in shape.Nodes)
            {
                var vectors = GetDirectionVectorsAtPoint(shape, node.Pt);
                if (vectors.Count < 2)
                    continue;

                evaluatedNodes++;

                // Compare every pair: use the smaller angle so "below 10°" = tight regardless of measurement side (10° or 350°)
                double minAngle = double.MaxValue;
                for (int i = 0; i < vectors.Count; i++)
                {
                    for (int j = i + 1; j < vectors.Count; j++)
                    {
                        double dot = Vector3d.Multiply(vectors[i], vectors[j]);
                        dot = Math.Clamp(dot, -1.0, 1.0);
                        double angRad = Math.Acos(dot);
                        double angDeg = SmallAngleDeg(angRad * (180.0 / Math.PI));
                        if (angDeg < minAngle) minAngle = angDeg;
                    }
                }

                // Map angle to penalty: <= minAngleDeg -> 1.0; >= optAngleDeg -> 0.0
                double nodePenalty = 0.0;
                if (minAngle < minAngleDeg)
                {
                    nodePenalty = 1.0;
                }
                else if (minAngle < optAngleDeg)
                {
                    // Linear ramp from 1 at minAngleDeg to 0 at optAngleDeg
                    nodePenalty = 1.0 - ((minAngle - minAngleDeg) / (optAngleDeg - minAngleDeg));
                }

                if (nodePenalty > 0.0)
                    violatingNodes++;

                sumNodePenalties += nodePenalty;
            }

            if (evaluatedNodes == 0)
            {
                res.VAng = 0.0;
                res.AngleViolationCount = 0;
                return res;
            }

            // Average penalty across evaluated nodes
            double avgPenalty = sumNodePenalties / evaluatedNodes;
            res.VAng = Math.Clamp(avgPenalty, 0.0, 1.0);
            res.AngleViolationCount = violatingNodes;
            return res;
        }

        /// <summary>
        /// Computes length-based penalty: 0–lenTooShort full penalty; lenTooShort–lenOptLow gradient to 0;
        /// lenOptLow–lenOptHigh no penalty; lenOptHigh–lenTooLong gradient up; >lenTooLong high penalty.
        /// </summary>
        private static FeasibilityResult ComputeLengthPenalty(SG_Shape shape,
            double lenTooShort = 0.5, double lenOptLow = 1.0, double lenOptHigh = 5.0, double lenTooLong = 12.0)
        {
            var res = new FeasibilityResult();

            if (shape == null || shape.Elems == null || shape.Elems.Count == 0)
            {
                res.VLen = 0.0;
                res.LengthViolationCount = 0;
                return res;
            }

            if (lenOptLow > lenOptHigh) lenOptHigh = lenOptLow + 0.1;
            if (lenTooLong <= lenOptHigh) lenTooLong = lenOptHigh + 1.0;

            int lenViolCount = 0;
            double sumPenalties = 0.0;

            foreach (var elem in shape.Elems)
            {
                if (elem == null) continue;

                double elen = 0.0;
                if (elem is Elements.SG_Elem1D e1d)
                {
                    if (e1d.Crv != null)
                    {
                        try { elen = e1d.Crv.GetLength(); }
                        catch { elen = 0.0; }
                    }
                    else if (e1d.Ln.IsValid)
                    {
                        elen = e1d.Ln.Length;
                    }
                }
                else
                    continue;

                double penalty = 0.0;
                if (elen <= 0.0)
                    penalty = 1.0;
                else if (elen < lenTooShort)
                    penalty = 1.0; // 0–lenTooShort: full punishment
                else if (elen < lenOptLow)
                    penalty = (lenOptLow - elen) / (lenOptLow - lenTooShort); // gradient to 0
                else if (elen <= lenOptHigh)
                    penalty = 0.0; // good range
                else if (elen <= lenTooLong)
                    penalty = (elen - lenOptHigh) / (lenTooLong - lenOptHigh); // gradient up
                else
                    penalty = 0.9; // quite high for > lenTooLong

                penalty = Math.Clamp(penalty, 0.0, 1.0);
                if (penalty > 0.0) lenViolCount++;
                sumPenalties += penalty;
            }

            double avg = (shape.Elems.Count > 0) ? (sumPenalties / shape.Elems.Count) : 0.0;
            res.VLen = Math.Clamp(avg, 0.0, 1.0);
            res.LengthViolationCount = lenViolCount;
            return res;
        }

        private const double INTERSECT_BAR_BAR_BASE = 1.0;
        private const double INTERSECT_STRUT_BAR_BASE = 2.0;
        private const double INTERSECT_MULTI_CROSS_INCREMENT = 0.3;
        /// <summary>Tolerance for "same endpoint": intersections at or within this distance of any segment endpoint are ignored (node connections, not true crossings).</summary>
        private const double INTERSECT_ENDPOINT_TOL = 1e-4;
        /// <summary>Only skip a pair when endpoints are this close (squared). Use tight value so bar-bar with distinct joints are not skipped.</summary>
        private const double SHARE_ENDPOINT_POSITION_TOL_SQ = 1e-20;

        /// <summary>
        /// Gets curve for intersection test: prefers node positions so rule-02 columns (Crv overwritten) use actual segment.
        /// </summary>
        private static Curve GetCurveForIntersection(Elements.SG_Elem1D e1)
        {
            if (e1?.Nodes != null && e1.Nodes.Length >= 2 && e1.Nodes[0] != null && e1.Nodes[1] != null)
                return new Line(e1.Nodes[0].Pt, e1.Nodes[1].Pt).ToNurbsCurve();
            if (e1?.Crv != null) return e1.Crv;
            if (e1?.Ln.IsValid == true) return e1.Ln.ToNurbsCurve();
            return null;
        }

        /// <summary>Returns base penalty for an intersection by element types. Used after finding every geometric crossing.</summary>
        private static double GetBasePenaltyForPair(Elem1DStructuralType ta, Elem1DStructuralType tb)
        {
            if (ta == Elem1DStructuralType.Strut && tb == Elem1DStructuralType.Bar) return INTERSECT_STRUT_BAR_BASE;
            if (ta == Elem1DStructuralType.Bar && tb == Elem1DStructuralType.Strut) return INTERSECT_STRUT_BAR_BASE;
            if (ta == Elem1DStructuralType.Bar && tb == Elem1DStructuralType.Bar) return INTERSECT_BAR_BAR_BASE;
            return INTERSECT_BAR_BAR_BASE;
        }

        /// <summary>
        /// Intersection data per event: actual 3D point and penalty contribution to feasibility.
        /// </summary>
        public struct IntersectionData
        {
            public Point3d Point;
            public double Value;
        }

        /// <summary>Element type counts for reverse-engineering when bar markers are lost (e.g. after section sync).</summary>
        public struct ElementTypeCounts
        {
            public int MainBeam;
            public int Strut;
            public int Bar;
            public int Other;
            public int Total => MainBeam + Strut + Bar + Other;
        }

        /// <summary>Returns how many elements are MainBeam, Strut, Bar, Other. Use to verify bar count and find when type is lost.</summary>
        public static ElementTypeCounts GetElementTypeCounts(SG_Shape shape)
        {
            var c = new ElementTypeCounts();
            if (shape?.Elems == null) return c;
            foreach (var e in shape.Elems)
            {
                if (!(e is Elements.SG_Elem1D e1)) continue;
                switch (e1.StructuralType)
                {
                    case Elem1DStructuralType.MainBeam: c.MainBeam++; break;
                    case Elem1DStructuralType.Strut: c.Strut++; break;
                    case Elem1DStructuralType.Bar: c.Bar++; break;
                    default: c.Other++; break;
                }
            }
            return c;
        }

        /// <summary>Returns (element, type, Autorule, Name) for each 1D element so you can see which are Other and why (e.g. Autorule 0).</summary>
        public static List<(Elements.SG_Elem1D Elem, Elem1DStructuralType Type, int Autorule, string Name)> GetElementTypeBreakdown(SG_Shape shape)
        {
            var list = new List<(Elements.SG_Elem1D, Elem1DStructuralType, int, string)>();
            if (shape?.Elems == null) return list;
            foreach (var e in shape.Elems)
            {
                if (!(e is Elements.SG_Elem1D e1)) continue;
                list.Add((e1, e1.StructuralType, e1.Autorule, e1.Name ?? ""));
            }
            return list;
        }

        /// <summary>Gets the segment line for intersection: element Ln if valid, else line from node positions.</summary>
        private static Line GetElementLine(Elements.SG_Elem1D e1)
        {
            if (e1 == null || e1.Nodes == null || e1.Nodes.Length < 2 || e1.Nodes[0] == null || e1.Nodes[1] == null)
                return Line.Unset;
            return e1.Ln.IsValid ? e1.Ln : new Line(e1.Nodes[0].Pt, e1.Nodes[1].Pt);
        }

        /// <summary>True if the two elements share a node (by ID) or have an endpoint at the same position (within tolSq). Use for strut-bar.</summary>
        private static bool ShareNodeOrEndpoint(Elements.SG_Elem1D a, Elements.SG_Elem1D b, double tolSq)
        {
            if (a?.Nodes == null || a.Nodes.Length < 2 || b?.Nodes == null || b.Nodes.Length < 2) return true;
            int na0 = a.Nodes[0].ID, na1 = a.Nodes[1].ID;
            int nb0 = b.Nodes[0].ID, nb1 = b.Nodes[1].ID;
            if (nb0 == na0 || nb0 == na1 || nb1 == na0 || nb1 == na1) return true;
            Point3d a0 = a.Nodes[0].Pt, a1 = a.Nodes[1].Pt, b0 = b.Nodes[0].Pt, b1 = b.Nodes[1].Pt;
            return a0.DistanceToSquared(b0) <= tolSq || a0.DistanceToSquared(b1) <= tolSq || a1.DistanceToSquared(b0) <= tolSq || a1.DistanceToSquared(b1) <= tolSq;
        }

        /// <summary>True only if an endpoint of a is at the same position as an endpoint of b (within tolSq). Ignores node ID so bar-bar pairs with shared IDs but distinct geometry still get intersection tested.</summary>
        private static bool ShareEndpointByPositionOnly(Elements.SG_Elem1D a, Elements.SG_Elem1D b, double tolSq)
        {
            if (a?.Nodes == null || a.Nodes.Length < 2 || b?.Nodes == null || b.Nodes.Length < 2) return true;
            Point3d a0 = a.Nodes[0].Pt, a1 = a.Nodes[1].Pt, b0 = b.Nodes[0].Pt, b1 = b.Nodes[1].Pt;
            return a0.DistanceToSquared(b0) <= tolSq || a0.DistanceToSquared(b1) <= tolSq || a1.DistanceToSquared(b0) <= tolSq || a1.DistanceToSquared(b1) <= tolSq;
        }

        /// <summary>
        /// Returns intersection locations (Point3d) and per-intersection penalty values. Main beam is skipped.
        /// Two loops: strut vs bar, then bar vs bar. Smaller lists and clearer logic for large structures.
        /// </summary>
        public static List<IntersectionData> GetIntersectionData(SG_Shape shape)
        {
            var outList = new List<IntersectionData>();
            if (shape?.Elems == null || shape.Elems.Count < 2) return outList;

            var struts = new List<Elements.SG_Elem1D>();
            var bars = new List<Elements.SG_Elem1D>();
            foreach (var e in shape.Elems)
            {
                if (!(e is Elements.SG_Elem1D e1) || e1.Nodes == null || e1.Nodes.Length < 2) continue;
                Line ln = GetElementLine(e1);
                if (!ln.IsValid) continue;
                switch (e1.StructuralType)
                {
                    case Elem1DStructuralType.Strut: struts.Add(e1); break;
                    case Elem1DStructuralType.Bar: bars.Add(e1); break;
                    default: break;
                }
            }

            const double lineLineTol = 1e-6;
            var rawIntersections = new List<(Point3d Pt, double BasePenalty, int StrutIdx, int BarIdxA, int BarIdxB)>();

            for (int si = 0; si < struts.Count; si++)
            {
                var a = struts[si];
                Line la = GetElementLine(a);
                if (!la.IsValid) continue;

                for (int bj = 0; bj < bars.Count; bj++)
                {
                    var b = bars[bj];
                    if (ShareNodeOrEndpoint(a, b, SHARE_ENDPOINT_POSITION_TOL_SQ)) continue;
                    Line lb = GetElementLine(b);
                    if (!lb.IsValid) continue;
                    if (!Intersection.LineLine(la, lb, out double pa, out double pb, lineLineTol, true)) continue;
                    if (pa <= 0.001 || pa >= 0.999 || pb <= 0.001 || pb >= 0.999) continue;
                    Point3d pt = la.PointAt(pa);
                    rawIntersections.Add((pt, INTERSECT_STRUT_BAR_BASE, si, bj, -1));
                }
            }

            for (int bi = 0; bi < bars.Count; bi++)
            {
                var a = bars[bi];
                Line la = GetElementLine(a);
                if (!la.IsValid) continue;

                for (int bj = bi + 1; bj < bars.Count; bj++)
                {
                    var b = bars[bj];
                    if (ShareEndpointByPositionOnly(a, b, SHARE_ENDPOINT_POSITION_TOL_SQ)) continue;
                    Line lb = GetElementLine(b);
                    if (!lb.IsValid) continue;
                    if (!Intersection.LineLine(la, lb, out double pa, out double pb, lineLineTol, true)) continue;
                    if (pa <= 0.001 || pa >= 0.999 || pb <= 0.001 || pb >= 0.999) continue;
                    Point3d pt = la.PointAt(pa);
                    rawIntersections.Add((pt, INTERSECT_BAR_BAR_BASE, -1, bi, bj));
                }
            }

            var crossingCountStrut = new int[struts.Count];
            var crossingCountBar = new int[bars.Count];
            foreach (var (_, _, strutIdx, barIdxA, barIdxB) in rawIntersections)
            {
                if (strutIdx >= 0) { crossingCountStrut[strutIdx]++; crossingCountBar[barIdxA]++; }
                else { crossingCountBar[barIdxA]++; crossingCountBar[barIdxB]++; }
            }

            foreach (var (pt, basePenalty, strutIdx, barIdxA, barIdxB) in rawIntersections)
            {
                int extraA = strutIdx >= 0 ? Math.Max(0, crossingCountStrut[strutIdx] - 1) : Math.Max(0, crossingCountBar[barIdxA] - 1);
                int extraB = strutIdx >= 0 ? Math.Max(0, crossingCountBar[barIdxA] - 1) : Math.Max(0, crossingCountBar[barIdxB] - 1);
                double extra = (extraA + extraB) * INTERSECT_MULTI_CROSS_INCREMENT;
                outList.Add(new IntersectionData { Point = pt, Value = basePenalty + extra });
            }

            return outList;
        }

        /// <summary>
        /// Penalizes qualifying intersections only (Bar–Bar and Strut–Bar). Strut–Bar worse than Bar–Bar; multiple crossings per bar increase penalty.
        /// </summary>
        private static FeasibilityResult ComputeIntersectionPenalty(SG_Shape shape)
        {
            var res = new FeasibilityResult();
            var data = GetIntersectionData(shape);
            res.IntersectionCount = data.Count;
            double totalValue = 0;
            foreach (var d in data) totalValue += d.Value;
            res.VIntersect = Math.Clamp(totalValue / Math.Max(1.0, 4.0), 0.0, 1.0);
            return res;
        }

        /// <summary>
        /// Penalizes low repetitiveness. Uses 10% length bins: elements within ±10% length go in same bin.
        /// VRepet low when many elements share few bins (good for manufacturing); high when many bins.
        /// </summary>
        private static FeasibilityResult ComputeRepetitivenessPenalty(SG_Shape shape, double binTolerancePercent = 10.0)
        {
            var res = new FeasibilityResult();
            if (shape?.Elems == null || shape.Elems.Count == 0)
            {
                res.VRepet = 0.0;
                res.RepetitivenessBinCount = 0;
                return res;
            }

            var lengths = new List<double>();
            foreach (var elem in shape.Elems)
            {
                if (elem is SG_Elem1D e1 && (e1.Ln.IsValid || (e1.Crv != null && e1.Crv.IsValid)))
                {
                    double len = e1.Ln.IsValid ? e1.Ln.Length : e1.Crv.GetLength();
                    if (len > 1e-10) lengths.Add(len);
                }
            }
            if (lengths.Count == 0)
            {
                res.VRepet = 0.0;
                res.RepetitivenessBinCount = 0;
                return res;
            }

            lengths.Sort();
            double factor = 1.0 + binTolerancePercent / 100.0;
            int binCount = 1;
            double binMin = lengths[0];

            for (int i = 1; i < lengths.Count; i++)
            {
                double L = lengths[i];
                if (L > factor * binMin)
                {
                    binCount++;
                    binMin = L;
                }
            }

            res.RepetitivenessBinCount = binCount;
            res.VRepet = Math.Clamp((double)binCount / lengths.Count, 0.0, 1.0);
            return res;
        }

        /// <summary>
        /// Penalizes duplicate elements (same line geometry). Rule 051 can produce duplicates; goal is zero.
        /// </summary>
        private static FeasibilityResult ComputeDuplicatePenalty(SG_Shape shape)
        {
            var res = new FeasibilityResult();
            if (shape?.Elems == null || shape.Elems.Count < 2)
            {
                res.VDup = 0.0;
                res.DuplicateCount = 0;
                return res;
            }

            const double tolSq = 1e-16;
            var elems1D = new List<SG_Elem1D>();
            foreach (var e in shape.Elems)
            {
                if (e is SG_Elem1D e1 && e1.Nodes != null && e1.Nodes.Length >= 2
                    && (e1.Ln.IsValid || (e1.Crv != null && e1.Crv.IsValid)))
                    elems1D.Add(e1);
            }

            int dupCount = 0;
            for (int i = 0; i < elems1D.Count; i++)
            {
                var a = elems1D[i];
                Point3d a0 = a.Nodes[0].Pt, a1 = a.Nodes[1].Pt;
                for (int j = i + 1; j < elems1D.Count; j++)
                {
                    var b = elems1D[j];
                    Point3d b0 = b.Nodes[0].Pt, b1 = b.Nodes[1].Pt;
                    bool matchFwd = a0.DistanceToSquared(b0) <= tolSq && a1.DistanceToSquared(b1) <= tolSq;
                    bool matchRev = a0.DistanceToSquared(b1) <= tolSq && a1.DistanceToSquared(b0) <= tolSq;
                    if (matchFwd || matchRev) dupCount++;
                }
            }

            res.DuplicateCount = dupCount;
            res.VDup = Math.Clamp((double)dupCount / Math.Max(1, elems1D.Count), 0.0, 1.0);
            return res;
        }

        /// <summary>
        /// Human-readable label for the feasibility penalty.
        /// </summary>
        public static string GetLabel()
        {
            return "Dangling Bar Penalty + Angle";
        }

        /// <summary>Returns the smaller angle in [0, 180] so that "below 10°" always means tight regardless of which side is measured (e.g. 10° or 350° both become 10°).</summary>
        private static double SmallAngleDeg(double angDeg)
        {
            if (double.IsNaN(angDeg) || double.IsInfinity(angDeg)) return angDeg;
            angDeg = ((angDeg % 360.0) + 360.0) % 360.0;
            return angDeg <= 180.0 ? angDeg : (360.0 - angDeg);
        }

        private const double NODE_SAME_POS_TOL = 1e-6;
        private static readonly double NODE_SAME_POS_TOL_SQ = NODE_SAME_POS_TOL * NODE_SAME_POS_TOL;

        /// <summary>Builds unit direction vectors for every element in shape.Elems that has an endpoint at pt (by geometry).
        /// This includes all rules (01, 02, 03, …); we do not use node.Elements so rule 03+ diagonals are never missed.</summary>
        private static List<Vector3d> GetDirectionVectorsAtPoint(SG_Shape shape, Point3d pt, double tolSq = -1)
        {
            if (tolSq < 0) tolSq = NODE_SAME_POS_TOL_SQ;
            var vectors = new List<Vector3d>();
            if (shape?.Elems == null) return vectors;
            foreach (var e in shape.Elems)
            {
                if (!(e is Elements.SG_Elem1D e1d) || e1d.Nodes == null || e1d.Nodes.Length < 2) continue;
                var n0 = e1d.Nodes[0];
                var n1 = e1d.Nodes[1];
                if (n0 == null || n1 == null) continue;
                Point3d other;
                if (n0.Pt.DistanceToSquared(pt) <= tolSq) other = n1.Pt;
                else if (n1.Pt.DistanceToSquared(pt) <= tolSq) other = n0.Pt;
                else continue;
                var v = other - pt;
                if (v.IsZero) continue;
                v.Unitize();
                bool duplicate = false;
                for (int k = 0; k < vectors.Count; k++)
                {
                    if (Math.Abs(Vector3d.Multiply(vectors[k], v)) > 0.9999)
                    { duplicate = true; break; }
                }
                if (!duplicate)
                    vectors.Add(v);
            }
            return vectors;
        }

        private static double ComputeBoundaryViolationRatio(SG_Shape shape)
        {
            if (shape?.Elems == null || shape.Elems.Count == 0) return 0.0;
            if (shape.BoundaryBrep == null && shape.BoundaryMesh == null) return 0.0;

            int total = 0;
            int outside = 0;

            foreach (var e in shape.Elems.OfType<Elements.SG_Elem1D>())
            {
                if (e?.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null)
                    continue;
                Line ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);
                if (!ln.IsValid || ln.Length < 1e-9) continue;

                total++;
                if (IsLineOutsideBoundary(ln, shape.BoundaryBrep, shape.BoundaryMesh))
                    outside++;
            }

            if (total == 0) return 0.0;
            return Math.Clamp((double)outside / total, 0.0, 1.0);
        }

        private static bool IsLineOutsideBoundary(Line ln, Brep brep, Mesh mesh)
        {
            var pts = new[]
            {
                ln.PointAt(0.25),
                ln.PointAt(0.5),
                ln.PointAt(0.75)
            };
            foreach (var p in pts)
            {
                if (!IsInsideBoundary(p, brep, mesh))
                    return true;
            }
            return false;
        }

        private static bool IsInsideBoundary(Point3d pt, Brep brep, Mesh mesh)
        {
            const double tol = 1e-6;
            if (brep != null) return brep.IsPointInside(pt, tol, false);
            if (mesh != null) return mesh.IsPointInside(pt, tol, false);
            return true;
        }

        // --- Visualization helpers ---

        /// <summary>Classification: 0=good, 1=orange (between), 2=bad, -1=no angle (degree&lt;2).</summary>
        public const int CLS_GOOD = 0;
        public const int CLS_ORANGE = 1;
        public const int CLS_BAD = 2;
        public const int CLS_NONE = -1;

        /// <summary>Returns (element, length[m], classification). Classification: 0=good, 1=orange, 2=bad.</summary>
        public static List<(Elements.SG_Elem1D Elem, double Length, int Classification)> GetElementLengthData(
            SG_Shape shape, double lenShort = 0.5, double lenOptLow = 1.0, double lenOptHigh = 5.0, double lenLong = 12.0)
        {
            var list = new List<(Elements.SG_Elem1D, double, int)>();
            if (shape?.Elems == null) return list;
            foreach (var e in shape.Elems)
            {
                if (!(e is Elements.SG_Elem1D e1)) continue;
                double elen = 0;
                if (e1.Crv != null) try { elen = e1.Crv.GetLength(); } catch { }
                else if (e1.Ln.IsValid) elen = e1.Ln.Length;
                int cls = CLS_GOOD;
                if (elen <= 0) cls = CLS_BAD;
                else if (elen < lenShort) cls = CLS_BAD;
                else if (elen < lenOptLow) cls = CLS_ORANGE;
                else if (elen <= lenOptHigh) cls = CLS_GOOD;
                else if (elen <= lenLong) cls = CLS_ORANGE;
                else cls = CLS_BAD;
                list.Add((e1, elen, cls));
            }
            return list;
        }

        /// <summary>Returns (element, binIndex, length) for 10% length bins (same as VRepet). Used for visualization by bin.</summary>
        public static (List<(Elements.SG_Elem1D Elem, int BinIndex, double Length)> Mappings, int TotalBinCount) GetElementLengthBinMapping(
            SG_Shape shape, double binTolerancePercent = 10.0)
        {
            var mappings = new List<(Elements.SG_Elem1D, int, double)>();
            if (shape?.Elems == null) return (mappings, 0);

            var withLen = new List<(Elements.SG_Elem1D Elem, double Length)>();
            foreach (var elem in shape.Elems)
            {
                if (!(elem is Elements.SG_Elem1D e1)) continue;
                double len = 0;
                if (e1.Ln.IsValid) len = e1.Ln.Length;
                else if (e1.Crv != null && e1.Crv.IsValid) try { len = e1.Crv.GetLength(); } catch { }
                if (len > 1e-10) withLen.Add((e1, len));
            }
            if (withLen.Count == 0) return (mappings, 0);

            withLen.Sort((a, b) => a.Length.CompareTo(b.Length));
            double factor = 1.0 + binTolerancePercent / 100.0;
            int binIndex = 0;
            double binMin = withLen[0].Length;

            foreach (var (Elem, Length) in withLen)
            {
                if (Length > factor * binMin)
                {
                    binIndex++;
                    binMin = Length;
                }
                mappings.Add((Elem, binIndex, Length));
            }

            int totalBins = binIndex + 1;
            return (mappings, totalBins);
        }

        /// <summary>Returns (node, minAngleDeg, classification) for all nodes with 2+ elements. Compares every element pair at each node. Classification: 0=good, 1=orange, 2=bad.</summary>
        public static List<(SG_Node Node, double MinAngleDeg, int Classification)> GetNodeAngleData(
            SG_Shape shape, double minDeg = 10.0, double optDeg = 30.0)
        {
            var list = new List<(SG_Node, double, int)>();
            if (shape?.Nodes == null) return list;
            foreach (var node in shape.Nodes)
            {
                var vectors = GetDirectionVectorsAtPoint(shape, node.Pt);
                if (vectors.Count < 2) continue;
                double minAngle = double.MaxValue;
                for (int i = 0; i < vectors.Count; i++)
                    for (int j = i + 1; j < vectors.Count; j++)
                    {
                        double dot = Math.Clamp(Vector3d.Multiply(vectors[i], vectors[j]), -1.0, 1.0);
                        double angDeg = SmallAngleDeg(Math.Acos(dot) * (180.0 / Math.PI));
                        if (angDeg < minAngle) minAngle = angDeg;
                    }
                int cls = minAngle >= optDeg ? CLS_GOOD : (minAngle >= minDeg ? CLS_ORANGE : CLS_BAD);
                list.Add((node, minAngle, cls));
            }
            return list;
        }

        /// <summary>Returns (node, minAngleDeg, classification) for every node in the structure.
        /// Nodes with &lt;2 elements get MinAngleDeg=NaN and Classification=CLS_NONE (-1).
        /// Use this to visualize analysis at all nodes (e.g. gray dot for no angle, colored for angle quality).</summary>
        public static List<(SG_Node Node, double MinAngleDeg, int Classification)> GetAllNodesAngleData(
            SG_Shape shape, double minDeg = 10.0, double optDeg = 30.0)
        {
            var list = new List<(SG_Node, double, int)>();
            if (shape?.Nodes == null) return list;
            foreach (var node in shape.Nodes)
            {
                var vectors = GetDirectionVectorsAtPoint(shape, node.Pt);
                if (vectors.Count < 2)
                {
                    list.Add((node, double.NaN, CLS_NONE));
                    continue;
                }
                double minAngle = double.MaxValue;
                for (int i = 0; i < vectors.Count; i++)
                    for (int j = i + 1; j < vectors.Count; j++)
                    {
                        double dot = Math.Clamp(Vector3d.Multiply(vectors[i], vectors[j]), -1.0, 1.0);
                        double angDeg = SmallAngleDeg(Math.Acos(dot) * (180.0 / Math.PI));
                        if (angDeg < minAngle) minAngle = angDeg;
                    }
                int cls = minAngle >= optDeg ? CLS_GOOD : (minAngle >= minDeg ? CLS_ORANGE : CLS_BAD);
                list.Add((node, minAngle, cls));
            }
            return list;
        }

        /// <summary>Returns intersection points at actual crossing locations. Only Bar–Bar and Strut–Bar (qualifying) pairs.</summary>
        public static List<Point3d> GetIntersectionPoints(SG_Shape shape)
        {
            return GetIntersectionData(shape).Select(x => x.Point).ToList();
        }

        /// <summary>Returns (element, midpoint) for elements with at least one endpoint of degree ≤ 1.</summary>
        public static List<(Elements.SG_Elem1D Elem, Point3d MidPoint)> GetDanglingElementMidpoints(SG_Shape shape)
        {
            var list = new List<(Elements.SG_Elem1D, Point3d)>();
            if (shape == null || shape.Elems == null || shape.Nodes == null) return list;
            var degree = new Dictionary<int, int>();
            foreach (var node in shape.Nodes)
                degree[node.ID] = node.Elements.Count;
            foreach (var elem in shape.Elems)
            {
                if (!(elem is Elements.SG_Elem1D e1) || elem.Nodes == null || elem.Nodes.Length < 2) continue;
                int du = degree.GetValueOrDefault(elem.Nodes[0].ID, 0);
                int dv = degree.GetValueOrDefault(elem.Nodes[1].ID, 0);
                if (du <= 1 || dv <= 1)
                {
                    Point3d mid = e1.Crv != null ? e1.Crv.PointAtNormalizedLength(0.5) : e1.Ln.PointAt(0.5);
                    list.Add((e1, mid));
                }
            }
            return list;
        }
    }
}