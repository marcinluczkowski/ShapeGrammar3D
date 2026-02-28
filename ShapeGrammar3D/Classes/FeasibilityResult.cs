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

        // --- Future feasibility components (placeholder) ---
        // public double VLen;   // element length violation
        // public double VAng;   // angle violation
        // public double VDeg;   // degree violation

        /// <summary>Weighted sum of all active feasibility components.</summary>
        public double TotalViolation;

        /// <summary>
        /// Human-readable summary for debugging / dataset export.
        /// </summary>
        public override string ToString()
        {
            return string.Format(
                "VDang={0:F4} VAng={1:F4} VLen={2:F4} (dangling={3}, isolated={4}, angleViol={5}, lenViol={6}), Total={7:F4}",
                VDang, VAng, VLen, DanglingEdgeCount, IsolatedEdgeCount, AngleViolationCount, LengthViolationCount, TotalViolation);
        }
    }
}