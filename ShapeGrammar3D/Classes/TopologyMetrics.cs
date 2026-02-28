using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
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
        SupportCount = 9,

        /// <summary>10 – Connected components (Betti-0, b₀). Discrete Morse: m₀ ≥ b₀.</summary>
        ConnectedComponents = 10,

        /// <summary>11 – Cycle rank (Betti-1, b₁ = E − V + b₀). Discrete Morse: m₁ ≥ b₁.</summary>
        CycleRank = 11,

        /// <summary>12 – Peak element crossings over concentric pipes around the main axis.</summary>
        MaxPipeIntersections = 12,

        /// <summary>13 – Mean element crossings over concentric pipes around the main axis.</summary>
        AvgPipeIntersections = 13
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
                TopoMetricType.ConnectedComponents  => ConnectedComponents(shape),
                TopoMetricType.CycleRank            => CycleRank(shape),
                TopoMetricType.MaxPipeIntersections => MaxPipeIntersections(shape),
                TopoMetricType.AvgPipeIntersections => AvgPipeIntersections(shape),
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
                TopoMetricType.ConnectedComponents  => "Connected Components (b₀)",
                TopoMetricType.CycleRank            => "Cycle Rank (b₁)",
                TopoMetricType.MaxPipeIntersections => "Max Pipe Intersections",
                TopoMetricType.AvgPipeIntersections => "Avg Pipe Intersections",
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

        /// <summary>
        /// 10 – Connected components (zeroth Betti number b₀).
        /// From Forman's discrete Morse theory, the weak Morse inequality
        /// states m₀ ≥ b₀: any discrete Morse function on the graph must
        /// have at least b₀ critical vertices.
        /// </summary>
        public static double ConnectedComponents(SG_Shape shape)
        {
            if (shape.Nodes.Count == 0) return 0;

            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var node in shape.Nodes)
                adj[node.ID] = new HashSet<int>();

            foreach (var elem in shape.Elems)
            {
                if (elem.Nodes == null || elem.Nodes.Length < 2
                    || elem.Nodes[0] == null || elem.Nodes[1] == null)
                    continue;

                int a = elem.Nodes[0].ID;
                int b = elem.Nodes[1].ID;
                if (adj.ContainsKey(a)) adj[a].Add(b);
                if (adj.ContainsKey(b)) adj[b].Add(a);
            }

            var visited = new HashSet<int>();
            int components = 0;

            foreach (var node in shape.Nodes)
            {
                if (visited.Contains(node.ID)) continue;
                components++;

                var queue = new Queue<int>();
                queue.Enqueue(node.ID);
                visited.Add(node.ID);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    if (!adj.ContainsKey(current)) continue;

                    foreach (int neighbor in adj[current])
                    {
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return components;
        }

        /// <summary>
        /// 11 – Cycle rank (first Betti number b₁ = E − V + b₀).
        /// Counts independent cycles in the graph. From the Morse
        /// inequalities (Forman, Theorem 2.11): m₁ ≥ b₁, so any
        /// discrete Morse function needs at least b₁ critical edges.
        /// For a tree b₁ = 0; each redundant bar adds one cycle.
        /// </summary>
        public static double CycleRank(SG_Shape shape)
        {
            double b0 = ConnectedComponents(shape);
            return shape.Elems.Count - shape.Nodes.Count + b0;
        }

        // --- Radial pipe intersection metrics (Morse-style filtration) ---

        private const int PIPE_SLICES = 20;

        /// <summary>
        /// 12 – Peak element crossings across concentric cylindrical
        /// surfaces (pipes) centered on the structure's main axis.
        /// </summary>
        public static double MaxPipeIntersections(SG_Shape shape)
        {
            int[] profile = ComputeRadialIntersectionProfile(shape, PIPE_SLICES);
            if (profile == null || profile.Length == 0) return 0;
            return profile.Max();
        }

        /// <summary>
        /// 13 – Mean element crossings across concentric cylindrical
        /// surfaces (pipes) centered on the structure's main axis.
        /// </summary>
        public static double AvgPipeIntersections(SG_Shape shape)
        {
            int[] profile = ComputeRadialIntersectionProfile(shape, PIPE_SLICES);
            if (profile == null || profile.Length == 0) return 0;
            return profile.Average();
        }

        /// <summary>
        /// Sweeps concentric cylinders outward from the main structural axis
        /// and counts how many element line segments cross each cylinder.
        /// An element crosses a cylinder at radius r when one endpoint is
        /// closer to the axis than r and the other is farther.
        /// </summary>
        private static int[] ComputeRadialIntersectionProfile(SG_Shape shape, int numSlices)
        {
            if (shape.Elems == null || shape.Elems.Count == 0
                || shape.Nodes == null || shape.Nodes.Count == 0)
                return null;

            var pts = shape.Nodes.Select(n => n.Pt).ToList();
            var centroid = new Point3d(
                pts.Average(p => p.X),
                pts.Average(p => p.Y),
                pts.Average(p => p.Z));

            Vector3d size = shape.Nodes.Aggregate(
                BoundingBox.Empty,
                (bb, n) => { bb.Union(n.Pt); return bb; },
                bb => bb.Max - bb.Min);

            Vector3d axis;
            if (size.X >= size.Y && size.X >= size.Z) axis = Vector3d.XAxis;
            else if (size.Y >= size.X && size.Y >= size.Z) axis = Vector3d.YAxis;
            else axis = Vector3d.ZAxis;

            var elemDists = new List<(double d0, double d1)>();
            double maxDist = 0;

            foreach (var elem in shape.Elems)
            {
                if (!(elem is SG_Elem1D elem1d)) continue;
                if (elem1d.Nodes == null || elem1d.Nodes.Length < 2
                    || elem1d.Nodes[0] == null || elem1d.Nodes[1] == null)
                    continue;

                double d0 = PerpDistanceToAxis(elem1d.Nodes[0].Pt, centroid, axis);
                double d1 = PerpDistanceToAxis(elem1d.Nodes[1].Pt, centroid, axis);
                elemDists.Add((d0, d1));
                maxDist = Math.Max(maxDist, Math.Max(d0, d1));
            }

            if (maxDist <= 0 || elemDists.Count == 0) return null;

            int[] crossings = new int[numSlices];
            for (int i = 0; i < numSlices; i++)
            {
                double r = maxDist * (i + 1.0) / (numSlices + 1.0);
                int count = 0;
                foreach (var (d0, d1) in elemDists)
                {
                    if ((d0 < r && d1 > r) || (d0 > r && d1 < r))
                        count++;
                }
                crossings[i] = count;
            }

            return crossings;
        }

        /// <summary>
        /// Perpendicular distance from a point to an axis line
        /// defined by an origin and a unit direction vector.
        /// </summary>
        private static double PerpDistanceToAxis(Point3d pt, Point3d axisOrigin, Vector3d axisDir)
        {
            Vector3d v = pt - axisOrigin;
            double proj = v.X * axisDir.X + v.Y * axisDir.Y + v.Z * axisDir.Z;
            Vector3d perp = v - axisDir * proj;
            return perp.Length;
        }
    }
}