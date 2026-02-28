using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Computes feasibility metrics for an SG_Shape graph structure.
    /// Designed to run after chromosome decoding and RegisterElemsToNodes(),
    /// before expensive FEM analysis.  The penalty is gentle: it does not
    /// discard individuals, only increases the feasibility violation score.
    /// </summary>
    public static class FeasibilityMetrics
    {
        /// <summary>
        /// Computes all feasibility components and returns the weighted total.
        /// </summary>
        /// <param name="shape">Decoded shape with registered node–element connectivity.</param>
        /// <param name="wDang">Weight for dangling bar penalty (small, e.g. 0.1–0.3).</param>
        /// <returns>Feasibility result with component breakdown and total violation.</returns>
        // New parameters: wAng and wLen weights for angle and length penalties (default disabled)
        public static FeasibilityResult Compute(SG_Shape shape, double wDang = 0.20, double wAng = 0.0, double wLen = 0.0)
        {
            var result = ComputeDanglingBarPenalty(shape);

            // Compute angle penalty component if requested
            var angResult = ComputeAnglePenalty(shape);
            result.VAng = angResult.VAng;
            result.AngleViolationCount = angResult.AngleViolationCount;

            // Compute length penalty
            var lenResult = ComputeLengthPenalty(shape);
            result.VLen = lenResult.VLen;
            result.LengthViolationCount = lenResult.LengthViolationCount;

            // Weighted sum of active components. Clamp to [0..1].
            double total = 0.0;
            total += Math.Clamp(wDang * result.VDang, 0.0, 1.0);
            total += Math.Clamp(wAng * result.VAng, 0.0, 1.0);
            total += Math.Clamp(wLen * result.VLen, 0.0, 1.0);
            result.TotalViolation = Math.Clamp(total, 0.0, 1.0);

            return result;
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
        /// Penalizes node configurations where the smallest angle between
        /// any two incident elements is below a minimum threshold (10°).
        /// Angles >= 30° are considered optimal and receive no penalty.
        /// The penalty per node is normalized to [0..1] by mapping angle
        /// from [minAngle, optAngle] -> [1,0]. The returned VAng is the
        /// average per-node penalty across all nodes, clamped to [0..1].
        /// </summary>
        private static FeasibilityResult ComputeAnglePenalty(SG_Shape shape)
        {
            var res = new FeasibilityResult();

            if (shape == null || shape.Nodes == null || shape.Nodes.Count == 0)
            {
                res.VAng = 0.0;
                res.AngleViolationCount = 0;
                return res;
            }

            const double minAngleDeg = 10.0; // below this -> full penalty
            const double optAngleDeg = 30.0; // at or above this -> no penalty

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
        /// Computes length-based penalty for elements.
        /// Elements shorter than minLen (0.5 m) receive a penalty up to 1.0 when very short.
        /// Elements longer than maxLen (10 m) receive a penalty up to 1.0 when very long.
        /// Between thresholds penalty ramps linearly to 0. The returned VLen is the
        /// average element penalty across all elements, clamped to [0..1].
        /// </summary>
        private static FeasibilityResult ComputeLengthPenalty(SG_Shape shape)
        {
            var res = new FeasibilityResult();

            if (shape == null || shape.Elems == null || shape.Elems.Count == 0)
            {
                res.VLen = 0.0;
                res.LengthViolationCount = 0;
                return res;
            }

            const double minLen = 0.5; // meters
            const double maxLen = 10.0; // meters
            // soften the falloff: values beyond these by factor produce stronger penalty
            const double extremeFactor = 0.25; // not used now but keep for tuning

            int lenViolCount = 0;
            double sumPenalties = 0.0;

            foreach (var elem in shape.Elems)
            {
                if (elem == null)
                {
                    continue;
                }

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
                {
                    // Unknown element type: skip
                    continue;
                }

                double penalty = 0.0;

                if (elen <= 0.0)
                    penalty = 1.0; // malformed
                else if (elen < minLen)
                {
                    // Shorter than minLen -> penalty from 1 at 0 to 0 at minLen
                    penalty = 1.0 - (elen / minLen);
                }
                else if (elen > maxLen)
                {
                    // Longer than maxLen -> penalty ramps up as (elen - maxLen)/maxLen, capped at 1
                    penalty = Math.Clamp((elen - maxLen) / maxLen, 0.0, 1.0);
                }

                if (penalty > 0.0)
                    lenViolCount++;

                sumPenalties += Math.Clamp(penalty, 0.0, 1.0);
            }

            double avg = (shape.Elems.Count > 0) ? (sumPenalties / shape.Elems.Count) : 0.0;
            res.VLen = Math.Clamp(avg, 0.0, 1.0);
            res.LengthViolationCount = lenViolCount;
            return res;
        }

        /// <summary>
        /// Human-readable label for the feasibility penalty.
        /// </summary>
        public static string GetLabel()
        {
            return "Dangling Bar Penalty + Angle";
        }
    }
}