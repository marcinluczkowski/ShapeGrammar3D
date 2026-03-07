using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Identifiers for available shape (geometry) metrics.
    /// Pass the integer value from Grasshopper to select a metric.
    /// </summary>
    public enum ShapeMetricType
    {
        /// <summary>0 – Sum of all element curve lengths (current default).</summary>
        TotalLength = 0,

        /// <summary>1 – Average element length.</summary>
        AvgLength = 1,

        /// <summary>2 – Longest element.</summary>
        MaxLength = 2,

        /// <summary>3 – Shortest element.</summary>
        MinLength = 3,

        /// <summary>4 – Standard deviation of element lengths.</summary>
        StdDevLength = 4,

        /// <summary>5 – Bounding-box volume of all node positions.</summary>
        BoundingBoxVolume = 5,

        /// <summary>6 – Bounding-box diagonal length.</summary>
        BoundingBoxDiagonal = 6,

        /// <summary>7 – Total structural volume (length × cross-section area per element).</summary>
        TotalStructuralVolume = 7,

        /// <summary>8 – Maximum distance between any two nodes (spatial spread).</summary>
        MaxNodeSpan = 8,

        /// <summary>9 – Total length / bounding-box diagonal (compactness).</summary>
        Compactness = 9,

        /// <summary>10 – Convex hull area in XY plane (footprint/envelope).</summary>
        ConvexHullAreaXY = 10,

        /// <summary>11 – Hull aspect ratio in XY (width/length, 1=square, &lt;1=elongated).</summary>
        HullAspectRatioXY = 11
    }

    /// <summary>
    /// Catalogue of shape (geometry) metrics that can be computed from an SG_Shape.
    /// Each metric returns a single double scalar for clustering.
    /// </summary>
    public static class ShapeMetrics
    {
        /// <summary>
        /// Total number of available metrics.
        /// </summary>
        public static int Count => Enum.GetValues(typeof(ShapeMetricType)).Length;

        /// <summary>
        /// Computes the selected shape metric for a shape.
        /// </summary>
        public static double Compute(SG_Shape shape, int metricType)
        {
            if (shape == null || shape.Elems == null)
                return 0.0;

            return (ShapeMetricType)metricType switch
            {
                ShapeMetricType.TotalLength           => TotalLength(shape),
                ShapeMetricType.AvgLength             => AvgLength(shape),
                ShapeMetricType.MaxLength             => MaxLength(shape),
                ShapeMetricType.MinLength             => MinLength(shape),
                ShapeMetricType.StdDevLength          => StdDevLength(shape),
                ShapeMetricType.BoundingBoxVolume     => BoundingBoxVolume(shape),
                ShapeMetricType.BoundingBoxDiagonal   => BoundingBoxDiagonal(shape),
                ShapeMetricType.TotalStructuralVolume => TotalStructuralVolume(shape),
                ShapeMetricType.MaxNodeSpan           => MaxNodeSpan(shape),
                ShapeMetricType.Compactness           => Compactness(shape),
                ShapeMetricType.ConvexHullAreaXY      => ConvexHullAreaXY(shape),
                ShapeMetricType.HullAspectRatioXY     => HullAspectRatioXY(shape),
                _ => TotalLength(shape)
            };
        }

        /// <summary>
        /// Returns a human-readable label for the given metric.
        /// </summary>
        public static string GetLabel(int metricType)
        {
            return (ShapeMetricType)metricType switch
            {
                ShapeMetricType.TotalLength           => "Total Length",
                ShapeMetricType.AvgLength             => "Avg Length",
                ShapeMetricType.MaxLength             => "Max Length",
                ShapeMetricType.MinLength             => "Min Length",
                ShapeMetricType.StdDevLength          => "StdDev Length",
                ShapeMetricType.BoundingBoxVolume     => "BBox Volume",
                ShapeMetricType.BoundingBoxDiagonal   => "BBox Diagonal",
                ShapeMetricType.TotalStructuralVolume => "Total Structural Volume",
                ShapeMetricType.MaxNodeSpan           => "Max Node Span",
                ShapeMetricType.Compactness           => "Compactness (L/diag)",
                ShapeMetricType.ConvexHullAreaXY      => "Hull Area XY",
                ShapeMetricType.HullAspectRatioXY     => "Hull Aspect XY",
                _ => "Unknown"
            };
        }

        // --- Helpers ---

        private static List<double> GetElementLengths(SG_Shape shape)
        {
            var lengths = new List<double>();
            foreach (var elem in shape.Elems)
            {
                if (elem is SG_Elem1D elem1d && elem1d.Crv != null)
                    lengths.Add(elem1d.Crv.GetLength());
            }
            return lengths;
        }

        private static BoundingBox GetNodeBoundingBox(SG_Shape shape)
        {
            if (shape.Nodes == null || shape.Nodes.Count == 0)
                return BoundingBox.Empty;

            var bb = new BoundingBox(shape.Nodes.Select(n => n.Pt));
            return bb;
        }

        // --- Individual metric implementations ---

        /// <summary>0 – Sum of all element curve lengths.</summary>
        public static double TotalLength(SG_Shape shape)
        {
            double total = 0.0;
            foreach (var elem in shape.Elems)
            {
                if (elem is SG_Elem1D elem1d && elem1d.Crv != null)
                    total += elem1d.Crv.GetLength();
            }
            return total;
        }

        /// <summary>1 – Mean element length.</summary>
        public static double AvgLength(SG_Shape shape)
        {
            var lengths = GetElementLengths(shape);
            return lengths.Count > 0 ? lengths.Average() : 0.0;
        }

        /// <summary>2 – Length of the longest element.</summary>
        public static double MaxLength(SG_Shape shape)
        {
            var lengths = GetElementLengths(shape);
            return lengths.Count > 0 ? lengths.Max() : 0.0;
        }

        /// <summary>3 – Length of the shortest element.</summary>
        public static double MinLength(SG_Shape shape)
        {
            var lengths = GetElementLengths(shape);
            return lengths.Count > 0 ? lengths.Min() : 0.0;
        }

        /// <summary>4 – Standard deviation of element lengths.</summary>
        public static double StdDevLength(SG_Shape shape)
        {
            var lengths = GetElementLengths(shape);
            if (lengths.Count < 2)
                return 0.0;

            double mean = lengths.Average();
            double sumSq = lengths.Sum(l => (l - mean) * (l - mean));
            return Math.Sqrt(sumSq / lengths.Count);
        }

        /// <summary>5 – Volume of the axis-aligned bounding box around all nodes.</summary>
        public static double BoundingBoxVolume(SG_Shape shape)
        {
            var bb = GetNodeBoundingBox(shape);
            if (!bb.IsValid)
                return 0.0;

            Vector3d diag = bb.Diagonal;
            return Math.Abs(diag.X * diag.Y * diag.Z);
        }

        /// <summary>6 – Diagonal length of the axis-aligned bounding box.</summary>
        public static double BoundingBoxDiagonal(SG_Shape shape)
        {
            var bb = GetNodeBoundingBox(shape);
            return bb.IsValid ? bb.Diagonal.Length : 0.0;
        }

        /// <summary>7 – Sum of (element length × cross-section area) for each element.</summary>
        public static double TotalStructuralVolume(SG_Shape shape)
        {
            double volume = 0.0;
            foreach (var elem in shape.Elems)
            {
                if (elem is SG_Elem1D elem1d && elem1d.Crv != null)
                {
                    double length = elem1d.Crv.GetLength();
                    double area = elem1d.CrossSection?.Area ?? 0.0;
                    volume += length * area;
                }
            }
            return volume;
        }

        /// <summary>8 – Maximum Euclidean distance between any two nodes.</summary>
        public static double MaxNodeSpan(SG_Shape shape)
        {
            if (shape.Nodes == null || shape.Nodes.Count < 2)
                return 0.0;

            double maxDist = 0.0;
            for (int i = 0; i < shape.Nodes.Count; i++)
            {
                for (int j = i + 1; j < shape.Nodes.Count; j++)
                {
                    double dist = shape.Nodes[i].Pt.DistanceTo(shape.Nodes[j].Pt);
                    if (dist > maxDist)
                        maxDist = dist;
                }
            }
            return maxDist;
        }

        /// <summary>9 – Total length / bounding-box diagonal. Higher = more material per span.</summary>
        public static double Compactness(SG_Shape shape)
        {
            double diag = BoundingBoxDiagonal(shape);
            return diag > 0.0 ? TotalLength(shape) / diag : 0.0;
        }

        /// <summary>10 – Convex hull area in XY plane (footprint/envelope).</summary>
        public static double ConvexHullAreaXY(SG_Shape shape)
        {
            var pts = GetXYPoints(shape);
            if (pts.Count < 3)
                return 0.0;

            var hull = ConvexHull2D(pts);
            if (hull.Count < 3)
                return 0.0;

            return PolygonArea2D(hull);
        }

        /// <summary>11 – Hull aspect ratio in XY (width/length, 1=square, &lt;1=elongated).</summary>
        public static double HullAspectRatioXY(SG_Shape shape)
        {
            var pts = GetXYPoints(shape);
            if (pts.Count < 3)
                return 0.0;

            var hull = ConvexHull2D(pts);
            if (hull.Count < 2)
                return 0.0;

            double minX = hull.Min(p => p.X);
            double maxX = hull.Max(p => p.X);
            double minY = hull.Min(p => p.Y);
            double maxY = hull.Max(p => p.Y);
            double w = maxX - minX;
            double h = maxY - minY;
            if (w <= 0 && h <= 0)
                return 0.0;
            double major = Math.Max(w, h);
            double minor = Math.Min(w, h);
            return major > 0 ? minor / major : 0.0;
        }

        private static List<Point2d> GetXYPoints(SG_Shape shape)
        {
            if (shape.Nodes == null || shape.Nodes.Count == 0)
                return new List<Point2d>();

            var pts = new List<Point2d>();
            foreach (var node in shape.Nodes)
            {
                if (node?.Pt != null)
                    pts.Add(new Point2d(node.Pt.X, node.Pt.Y));
            }
            return pts;
        }

        /// <summary>Graham scan 2D convex hull. Returns vertices in CCW order.</summary>
        private static List<Point2d> ConvexHull2D(List<Point2d> pts)
        {
            if (pts == null || pts.Count < 3)
                return new List<Point2d>();

            var sorted = pts.Distinct().OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
            if (sorted.Count < 3)
                return sorted;

            Point2d pivot = sorted[0];

            var byAngle = sorted.Skip(1)
                .OrderBy(p => Math.Atan2(p.Y - pivot.Y, p.X - pivot.X))
                .ThenBy(p => pivot.DistanceTo(p))
                .ToList();

            var hull = new List<Point2d> { pivot, byAngle[0] };

            for (int i = 1; i < byAngle.Count; i++)
            {
                Point2d next = byAngle[i];
                while (hull.Count >= 2 && Cross2D(hull[hull.Count - 2], hull[hull.Count - 1], next) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(next);
            }

            return hull;
        }

        private static double Cross2D(Point2d o, Point2d a, Point2d b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>Shoelace formula for polygon area.</summary>
        private static double PolygonArea2D(List<Point2d> pts)
        {
            if (pts == null || pts.Count < 3)
                return 0.0;

            double area = 0.0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
            return Math.Abs(area) * 0.5;
        }
    }
}