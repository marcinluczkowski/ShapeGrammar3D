using System;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Breakdown of feasibility violation components for a single individual.
    /// All component penalties (VXxx) are normalized to [0..1].
    /// TotalViolation is the weighted sum of active components.
    /// </summary>
    public struct FeasibilityResult
    {
        /// <summary>Dangling bar penalty [0..1].</summary>
        public double VDang;

        /// <summary>Angle-based penalty [0..1]. Penalizes very small angles at nodes.</summary>
        public double VAng;

        /// <summary>Length-based penalty [0..1]. Penalizes elements that are too short or too long.</summary>
        public double VLen;

        /// <summary>Number of edges where at least one endpoint has degree ≤ 1.</summary>
        public int DanglingEdgeCount;

        /// <summary>Number of edges where both endpoints have degree ≤ 1.</summary>
        public int IsolatedEdgeCount;

        /// <summary>Number of nodes with angle violations (min angle &lt; threshold).</summary>
        public int AngleViolationCount;

        /// <summary>Number of elements with length violations (too short or too long).</summary>
        public int LengthViolationCount;

        /// <summary>Intersection penalty [0..1]. Penalizes bracing/column elements that cross each other.</summary>
        public double VIntersect;

        /// <summary>Number of element pairs that intersect (excluding shared nodes).</summary>
        public int IntersectionCount;

        /// <summary>Repetitiveness penalty [0..1]. Lower when more elements share similar lengths (10% bins). Favours manufacturing.</summary>
        public double VRepet;

        /// <summary>Number of distinct length bins (10% tolerance). Fewer = more repetitive.</summary>
        public int RepetitivenessBinCount;

        /// <summary>Duplicate-element penalty [0..1]. Zero when no duplicates; penalizes elements with identical geometry.</summary>
        public double VDup;

        /// <summary>Number of duplicate element pairs (same line geometry, e.g. from rule 051).</summary>
        public int DuplicateCount;

        /// <summary>Weighted sum of all active feasibility components.</summary>
        public double TotalViolation;

        /// <summary>
        /// Human-readable summary for debugging / dataset export.
        /// </summary>
        public override string ToString()
        {
            return string.Format(
                "VDang={0:F4} VAng={1:F4} VLen={2:F4} VInt={3:F4} VRepet={4:F4} VDup={5:F4} (dangling={6}, isolated={7}, angleViol={8}, lenViol={9}, intersect={10}, bins={11}, dup={12}), Total={13:F4}",
                VDang, VAng, VLen, VIntersect, VRepet, VDup, DanglingEdgeCount, IsolatedEdgeCount, AngleViolationCount, LengthViolationCount, IntersectionCount, RepetitivenessBinCount, DuplicateCount, TotalViolation);
        }
    }
}