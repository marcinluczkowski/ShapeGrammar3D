using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

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

            double total = 0.0;
            total += Math.Clamp(s.WDang * result.VDang, 0.0, 1.0);
            total += Math.Clamp(s.WAng * result.VAng, 0.0, 1.0);
            total += Math.Clamp(s.WLen * result.VLen, 0.0, 1.0);
            total += Math.Clamp(s.WIntersect * result.VIntersect, 0.0, 1.0);
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
        /// Computes angle-based penalty at nodes.
        /// 0–minAngleDeg: full penalty; minAngleDeg–optAngleDeg: linear gradient to zero; ≥optAngleDeg: no penalty.
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
                if (node.Elements == null || node.Elements.Count < 2)
                    continue; // no angle to evaluate

                // Build vectors for incident elements at this node
                var vectors = new List<Vector3d>();
                foreach (var elem in node.Elements)
                {
                    // Each element should be SG_Elem1D
                    if (elem is Elements.SG_Elem1D e1d)
                    {
                        // Determine the other node point to form vector
                        var n0 = e1d.Nodes?[0];
                        var n1 = e1d.Nodes?[1];
                        Point3d otherPt;
                        if (n0 == null || n1 == null)
                            continue;

                        if (n0.ID == node.ID)
                            otherPt = n1.Pt;
                        else
                            otherPt = n0.Pt;

                        var v = otherPt - node.Pt;
                        if (v.IsZero)
                            continue;
                        v.Unitize();
                        vectors.Add(v);
                    }
                }

                if (vectors.Count < 2)
                    continue;

                evaluatedNodes++;

                // Find smallest angle between any two vectors
                double minAngle = double.MaxValue; // degrees
                for (int i = 0; i < vectors.Count; i++)
                {
                    for (int j = i + 1; j < vectors.Count; j++)
                    {
                        double dot = Vector3d.Multiply(vectors[i], vectors[j]);
                        dot = Math.Clamp(dot, -1.0, 1.0);
                        double angRad = Math.Acos(dot);
                        double angDeg = angRad * (180.0 / Math.PI);
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

        /// <summary>
        /// Penalizes intersecting element pairs (bracing/column crossing). Excludes pairs that share a node.
        /// VIntersect = normalized count (bounded 0..1) so simple beams get low score.
        /// </summary>
        private static FeasibilityResult ComputeIntersectionPenalty(SG_Shape shape)
        {
            var res = new FeasibilityResult();
            if (shape == null || shape.Elems == null || shape.Elems.Count < 2)
            {
                res.VIntersect = 0.0;
                res.IntersectionCount = 0;
                return res;
            }

            var elems1d = new List<Elements.SG_Elem1D>();
            foreach (var e in shape.Elems)
            {
                if (e is Elements.SG_Elem1D e1 && e1.Crv != null && e1.Nodes != null && e1.Nodes.Length >= 2)
                    elems1d.Add(e1);
            }

            int intersectCount = 0;
            for (int i = 0; i < elems1d.Count; i++)
            {
                var a = elems1d[i];
                Curve ca = a.Crv;
                var na0 = a.Nodes[0].ID;
                var na1 = a.Nodes[1].ID;

                for (int j = i + 1; j < elems1d.Count; j++)
                {
                    var b = elems1d[j];
                    if (b.Nodes[0].ID == na0 || b.Nodes[0].ID == na1 || b.Nodes[1].ID == na0 || b.Nodes[1].ID == na1)
                        continue; // share a node
                    Curve cb = b.Crv;
                    var events = Intersection.CurveCurve(ca, cb, 1e-6, 1e-6);
                    if (events != null && events.Count > 0)
                        intersectCount++;
                }
            }

            res.IntersectionCount = intersectCount;
            int maxPairs = (elems1d.Count * (elems1d.Count - 1)) / 2;
            res.VIntersect = maxPairs > 0 ? Math.Clamp((double)intersectCount / Math.Max(1, maxPairs / 4), 0.0, 1.0) : 0.0;
            return res;
        }

        /// <summary>
        /// Human-readable label for the feasibility penalty.
        /// </summary>
        public static string GetLabel()
        {
            return "Dangling Bar Penalty + Angle";
        }

        // --- Visualization helpers ---

        /// <summary>Classification: 0=good, 1=orange (between), 2=bad.</summary>
        public const int CLS_GOOD = 0;
        public const int CLS_ORANGE = 1;
        public const int CLS_BAD = 2;

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

        /// <summary>Returns (node, minAngleDeg, classification). Classification: 0=good, 1=orange, 2=bad.</summary>
        public static List<(SG_Node Node, double MinAngleDeg, int Classification)> GetNodeAngleData(
            SG_Shape shape, double minDeg = 10.0, double optDeg = 30.0)
        {
            var list = new List<(SG_Node, double, int)>();
            if (shape?.Nodes == null) return list;
            foreach (var node in shape.Nodes)
            {
                if (node.Elements == null || node.Elements.Count < 2) continue;
                var vectors = new List<Vector3d>();
                foreach (var elem in node.Elements)
                {
                    if (!(elem is Elements.SG_Elem1D e1d) || e1d.Nodes == null) continue;
                    var n0 = e1d.Nodes[0]; var n1 = e1d.Nodes[1];
                    if (n0 == null || n1 == null) continue;
                    Point3d otherPt = n0.ID == node.ID ? n1.Pt : n0.Pt;
                    var v = otherPt - node.Pt;
                    if (v.IsZero) continue;
                    v.Unitize();
                    vectors.Add(v);
                }
                if (vectors.Count < 2) continue;
                double minAngle = double.MaxValue;
                for (int i = 0; i < vectors.Count; i++)
                    for (int j = i + 1; j < vectors.Count; j++)
                    {
                        double dot = Math.Clamp(Vector3d.Multiply(vectors[i], vectors[j]), -1.0, 1.0);
                        double angDeg = Math.Acos(dot) * (180.0 / Math.PI);
                        if (angDeg < minAngle) minAngle = angDeg;
                    }
                int cls = minAngle >= optDeg ? CLS_GOOD : (minAngle >= minDeg ? CLS_ORANGE : CLS_BAD);
                list.Add((node, minAngle, cls));
            }
            return list;
        }

        /// <summary>Returns intersection points (midpoint of overlap) for non-adjacent element pairs.</summary>
        public static List<Point3d> GetIntersectionPoints(SG_Shape shape)
        {
            var pts = new List<Point3d>();
            if (shape?.Elems == null || shape.Elems.Count < 2) return pts;
            var elems1d = new List<Elements.SG_Elem1D>();
            foreach (var e in shape.Elems)
            {
                if (e is Elements.SG_Elem1D e1 && e1.Crv != null && e1.Nodes != null && e1.Nodes.Length >= 2)
                    elems1d.Add(e1);
            }
            for (int i = 0; i < elems1d.Count; i++)
            {
                var a = elems1d[i];
                int na0 = a.Nodes[0].ID, na1 = a.Nodes[1].ID;
                for (int j = i + 1; j < elems1d.Count; j++)
                {
                    var b = elems1d[j];
                    if (b.Nodes[0].ID == na0 || b.Nodes[0].ID == na1 || b.Nodes[1].ID == na0 || b.Nodes[1].ID == na1)
                        continue;
                    var events = Intersection.CurveCurve(a.Crv, b.Crv, 1e-6, 1e-6);
                    if (events != null)
                        foreach (var ev in events)
                        {
                            if (ev != null && ev.IsPoint)
                                pts.Add(ev.PointA);
                        }
                }
            }
            return pts;
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