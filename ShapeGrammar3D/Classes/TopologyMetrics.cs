using System;
using System.Collections.Generic;
using System.Linq;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Identifiers for available topology metrics.
    /// Pass the integer value from Grasshopper to select a metric.
    /// </summary>
    public enum TopoMetricType
    {
        /// <summary>0 – Number of elements (current default).</summary>
        ElementCount = 0,

        /// <summary>1 – Number of nodes.</summary>
        NodeCount = 1,

        /// <summary>2 – Element-to-node ratio (graph density).</summary>
        ElementNodeRatio = 2,

        /// <summary>3 – Average node valence (mean connections per node).</summary>
        AvgValence = 3,

        /// <summary>4 – Maximum node valence (most-connected node).</summary>
        MaxValence = 4,

        /// <summary>5 – Number of leaf nodes (valence == 1, open ends).</summary>
        LeafNodeCount = 5,

        /// <summary>6 – Number of branch nodes (valence &gt;= 3).</summary>
        BranchNodeCount = 6,

        /// <summary>7 – Euler characteristic V − E (graph invariant).</summary>
        EulerCharacteristic = 7,

        /// <summary>8 – Number of distinct element names (rule diversity).</summary>
        DistinctElementNames = 8,

        /// <summary>9 – Number of supported nodes.</summary>
        SupportCount = 9
    }

    /// <summary>
    /// Catalogue of topology metrics that can be computed from an SG_Shape.
    /// Each metric returns a single double scalar for clustering.
    /// </summary>
    public static class TopologyMetrics
    {
        /// <summary>
        /// Total number of available metrics.
        /// </summary>
        public static int Count => Enum.GetValues(typeof(TopoMetricType)).Length;

        /// <summary>
        /// Computes the selected topology metric for a shape.
        /// </summary>
        /// <param name="shape">The shape to evaluate.</param>
        /// <param name="metricType">Integer metric selector (see <see cref="TopoMetricType"/>).</param>
        /// <returns>Metric value, or 0 if the shape is invalid.</returns>
        public static double Compute(SG_Shape shape, int metricType)
        {
            if (shape == null || shape.Elems == null || shape.Nodes == null)
                return 0.0;

            return (TopoMetricType)metricType switch
            {
                TopoMetricType.ElementCount        => ElementCount(shape),
                TopoMetricType.NodeCount           => NodeCount(shape),
                TopoMetricType.ElementNodeRatio    => ElementNodeRatio(shape),
                TopoMetricType.AvgValence          => AvgValence(shape),
                TopoMetricType.MaxValence          => MaxValence(shape),
                TopoMetricType.LeafNodeCount       => LeafNodeCount(shape),
                TopoMetricType.BranchNodeCount     => BranchNodeCount(shape),
                TopoMetricType.EulerCharacteristic => EulerCharacteristic(shape),
                TopoMetricType.DistinctElementNames => DistinctElementNames(shape),
                TopoMetricType.SupportCount        => SupportCount(shape),
                _ => ElementCount(shape)
            };
        }

        /// <summary>
        /// Returns a human-readable label for the given metric.
        /// </summary>
        public static string GetLabel(int metricType)
        {
            return (TopoMetricType)metricType switch
            {
                TopoMetricType.ElementCount         => "Element Count",
                TopoMetricType.NodeCount            => "Node Count",
                TopoMetricType.ElementNodeRatio     => "Element/Node Ratio",
                TopoMetricType.AvgValence           => "Avg Node Valence",
                TopoMetricType.MaxValence           => "Max Node Valence",
                TopoMetricType.LeafNodeCount        => "Leaf Node Count",
                TopoMetricType.BranchNodeCount      => "Branch Node Count",
                TopoMetricType.EulerCharacteristic  => "Euler Characteristic (V-E)",
                TopoMetricType.DistinctElementNames => "Distinct Element Names",
                TopoMetricType.SupportCount         => "Support Count",
                _ => "Unknown"
            };
        }

        // --- Individual metric implementations ---

        /// <summary>0 – Total number of elements.</summary>
        public static double ElementCount(SG_Shape shape)
            => shape.Elems.Count;

        /// <summary>1 – Total number of nodes.</summary>
        public static double NodeCount(SG_Shape shape)
            => shape.Nodes.Count;

        /// <summary>2 – Elements / Nodes ratio. Higher = denser graph.</summary>
        public static double ElementNodeRatio(SG_Shape shape)
            => shape.Nodes.Count > 0
                ? (double)shape.Elems.Count / shape.Nodes.Count
                : 0.0;

        /// <summary>3 – Average number of elements per node.</summary>
        public static double AvgValence(SG_Shape shape)
            => shape.Nodes.Count > 0
                ? shape.Nodes.Average(n => n.Elements.Count)
                : 0.0;

        /// <summary>4 – Highest valence node (most connections).</summary>
        public static double MaxValence(SG_Shape shape)
            => shape.Nodes.Count > 0
                ? shape.Nodes.Max(n => n.Elements.Count)
                : 0.0;

        /// <summary>5 – Nodes with exactly 1 connected element (open ends).</summary>
        public static double LeafNodeCount(SG_Shape shape)
            => shape.Nodes.Count(n => n.Elements.Count == 1);

        /// <summary>6 – Nodes with 3+ connected elements (branching points).</summary>
        public static double BranchNodeCount(SG_Shape shape)
            => shape.Nodes.Count(n => n.Elements.Count >= 3);

        /// <summary>7 – V − E. For a tree: always 1. Cycles make it ≤ 0.</summary>
        public static double EulerCharacteristic(SG_Shape shape)
            => shape.Nodes.Count - shape.Elems.Count;

        /// <summary>8 – Number of distinct element Name values (rule diversity).</summary>
        public static double DistinctElementNames(SG_Shape shape)
            => shape.Elems
                .Where(e => e.Name != null)
                .Select(e => e.Name)
                .Distinct()
                .Count();

        /// <summary>9 – Number of supported nodes.</summary>
        public static double SupportCount(SG_Shape shape)
            => shape.Supports?.Count ?? 0.0;
    }
}