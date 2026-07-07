using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rhino.Geometry;
using Grasshopper.Kernel;
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
        HullAspectRatioXY = 11,

        /// <summary>12 – Area from mesh created from lines (JoinCurves + planar breps). More accurate than hull.</summary>
        MeshAreaFromLines = 12,

        /// <summary>13 – Convex hull volume (3D envelope).</summary>
        ConvexHullVolume = 13,

        /// <summary>14 – ShrinkWrap volume (detail-following envelope).</summary>
        ShrinkWrapVolume = 14
    }

    /// <summary>
    /// Catalogue of shape (geometry) metrics that can be computed from an SG_Shape.
    /// Each metric returns a single double scalar for clustering.
    /// </summary>
    public static class ShapeMetrics
    {
        public const double DefaultShrinkWrapDetailRatio = 0.02;
        /// <summary>
        /// Total number of available metrics.
        /// </summary>
        public static int Count => Enum.GetValues(typeof(ShapeMetricType)).Length;

        /// <summary>
        /// Computes the selected shape metric for a shape.
        /// </summary>
        public static double Compute(SG_Shape shape, int metricType)
        {
            return Compute(shape, metricType, DefaultShrinkWrapDetailRatio);
        }

        public static double Compute(SG_Shape shape, int metricType, double shrinkWrapDetailRatio)
        {
            return Compute(shape, metricType, shrinkWrapDetailRatio, fastMode: false);
        }

        /// <summary>
        /// Computes the selected shape metric.  When <paramref name="fastMode"/> is <c>true</c>
        /// expensive Rhino mesh operations (currently only <see cref="ShapeMetricType.ShrinkWrapVolume"/>)
        /// run with coarse parameters so they fit inside a tight GA inner loop.  All other metrics
        /// are unaffected because they are already O(N) on element/node lists.
        /// </summary>
        public static double Compute(SG_Shape shape, int metricType, double shrinkWrapDetailRatio, bool fastMode)
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
                ShapeMetricType.MeshAreaFromLines     => MeshAreaFromLines(shape),
                ShapeMetricType.ConvexHullVolume      => ConvexHullVolume(shape),
                ShapeMetricType.ShrinkWrapVolume      => ShrinkWrapVolume(shape, shrinkWrapDetailRatio, fastMode),
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
                ShapeMetricType.ConvexHullAreaXY      => "Hull Area (struct. footprint)",
                ShapeMetricType.HullAspectRatioXY     => "Hull Aspect (struct. footprint)",
                ShapeMetricType.MeshAreaFromLines     => "Mesh Area (struct. footprint)",
                ShapeMetricType.ConvexHullVolume      => "Convex Hull Volume",
                ShapeMetricType.ShrinkWrapVolume      => "ShrinkWrap Volume",
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

        /// <summary>10 – Convex hull of node footprint on the structural horizontal plane (world Z normal, X along initial RULE010 / first beam direction).</summary>
        public static double ConvexHullAreaXY(SG_Shape shape)
        {
            var pts = GetStructuralFootprint2DPoints(shape);
            if (pts.Count < 3)
                return 0.0;

            var hull = ConvexHull2D(pts);
            if (hull.Count < 3)
                return 0.0;

            return PolygonArea2D(hull);
        }

        /// <summary>11 – Hull aspect on structural footprint plane (1=square, &lt;1=elongated).</summary>
        public static double HullAspectRatioXY(SG_Shape shape)
        {
            var pts = GetStructuralFootprint2DPoints(shape);
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

        /// <summary>12 – Planar face area from projected line network on the structural footprint plane.</summary>
        public static double MeshAreaFromLines(SG_Shape shape)
        {
            if (shape?.Elems == null || shape.Elems.Count == 0) return 0.0;
            var (area, _) = MeshAreaFromLinesWithMesh(shape);
            return area;
        }

        /// <summary>
        /// Plane for hull / mesh-from-lines metrics: X along the horizontal part of the initial RULE010 beam
        /// (else first 1D curve). Y is chosen so projections are not degenerate — either horizontal plan
        /// (normal ≈ world Z) or a vertical plane through that beam and world-up (e.g. XZ layouts with constant Y).
        /// </summary>
        public static bool TryGetStructuralFootprintPlane(SG_Shape shape, out Plane plane)
        {
            plane = Plane.Unset;
            if (shape?.Nodes == null || shape.Nodes.Count == 0)
                return false;

            Point3d origin = Point3d.Origin;
            int c = 0;
            foreach (var n in shape.Nodes)
            {
                if (n == null) continue;
                origin += n.Pt;
                c++;
            }
            if (c == 0) return false;
            origin /= c;

            var nodePts = new List<Point3d>(c);
            foreach (var n in shape.Nodes)
            {
                if (n == null) continue;
                nodePts.Add(n.Pt);
            }

            Vector3d tBeam = GetInitialBeamTangent3D(shape);
            Vector3d zUp = Vector3d.ZAxis;

            Vector3d beamX = tBeam - zUp * (tBeam * zUp);
            if (beamX.Length < 1e-9)
            {
                beamX = Vector3d.CrossProduct(Vector3d.XAxis, tBeam);
                if (beamX.Length < 1e-9)
                    beamX = Vector3d.CrossProduct(Vector3d.YAxis, tBeam);
                if (beamX.Length < 1e-9)
                    return false;
            }
            beamX.Unitize();

            Vector3d yPlan = Vector3d.CrossProduct(zUp, beamX);
            if (yPlan.Length < 1e-9)
                yPlan = Vector3d.YAxis;
            else
                yPlan.Unitize();

            Vector3d yElev = zUp - beamX * (zUp * beamX);
            if (yElev.Length < 1e-9)
            {
                yElev = Vector3d.CrossProduct(beamX, tBeam);
                if (yElev.Length < 1e-9)
                    yElev = zUp;
            }
            yElev.Unitize();

            double extentPlan = FootprintAxisAlignedRectArea(origin, beamX, yPlan, nodePts);
            double extentElev = FootprintAxisAlignedRectArea(origin, beamX, yElev, nodePts);

            Vector3d yAxis = extentElev > extentPlan * (1.0 + 1e-9) ? yElev : yPlan;
            plane = new Plane(origin, beamX, yAxis);
            return plane.IsValid;
        }

        /// <summary>Product of (max−min) extents along two orthonormal axes through <paramref name="origin"/>.</summary>
        private static double FootprintAxisAlignedRectArea(Point3d origin, Vector3d xDir, Vector3d yDir, List<Point3d> pts)
        {
            if (pts == null || pts.Count == 0) return 0;
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in pts)
            {
                Vector3d d = p - origin;
                double u = d * xDir;
                double v = d * yDir;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
            return Math.Max(0, maxU - minU) * Math.Max(0, maxV - minV);
        }

        /// <summary>Unit tangent of the “main” beam: RULE010 <c>Init_Crv</c>, else first 1D curve or chord.</summary>
        private static Vector3d GetInitialBeamTangent3D(SG_Shape shape)
        {
            var beam = shape.Elems?.OfType<SG_Elem1D>()
                .FirstOrDefault(e => e.Autorule == UT.RULE010_MARKER && e.Init_Crv != null);
            if (beam == null)
                beam = shape.Elems?.OfType<SG_Elem1D>().FirstOrDefault(e => e.Crv != null);
            if (beam == null)
                beam = shape.Elems?.OfType<SG_Elem1D>().FirstOrDefault();

            Vector3d d = Vector3d.XAxis;
            if (beam?.Init_Crv != null)
            {
                var crv = beam.Init_Crv;
                double t = crv.Domain.ParameterAt(0.5);
                d = crv.TangentAt(t);
            }
            else if (beam?.Crv != null)
            {
                var crv = beam.Crv;
                double t = crv.Domain.ParameterAt(0.5);
                d = crv.TangentAt(t);
            }
            else if (beam?.Nodes != null && beam.Nodes.Length >= 2
                && beam.Nodes[0] != null && beam.Nodes[1] != null)
            {
                d = beam.Nodes[1].Pt - beam.Nodes[0].Pt;
            }

            if (d.Length < 1e-12)
                return Vector3d.XAxis;
            d.Unitize();
            return d;
        }

        private static List<Point2d> GetStructuralFootprint2DPoints(SG_Shape shape)
        {
            if (shape?.Nodes == null || shape.Nodes.Count == 0)
                return new List<Point2d>();
            if (!TryGetStructuralFootprintPlane(shape, out Plane pl) || !pl.IsValid)
                return new List<Point2d>();

            var pts = new List<Point2d>(shape.Nodes.Count);
            foreach (var node in shape.Nodes)
            {
                if (node == null) continue;
                pl.ClosestParameter(node.Pt, out double u, out double v);
                pts.Add(new Point2d(u, v));
            }
            return pts;
        }

        /// <summary>13 – Convex Hull Volume. Calculate envelope volume generated by Grasshopper's convex hull volume.</summary>
        public static double ConvexHullVolume(SG_Shape shape)
        {
            var mesh = ConvexHullMesh(shape);
            if (mesh == null || !mesh.IsValid) return 0.0;

            try
            {
                var volumeProps = VolumeMassProperties.Compute(mesh);
                return Math.Abs(volumeProps?.Volume ?? 0.0);
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Builds the same convex-hull mesh that <see cref="ConvexHullVolume"/>
        /// uses internally. Returns <c>null</c> when the node set is too small
        /// or the hull cannot be computed. Exposed so downstream components
        /// (e.g. a hull-shape preview) can render exactly what metric 13 sees.
        /// </summary>
        public static Mesh ConvexHullMesh(SG_Shape shape)
        {
            if (shape?.Elems == null || shape.Elems.Count == 0) return null;
            if (shape.Nodes == null || shape.Nodes.Count < 4) return null;

            try
            {
                var pts = shape.Nodes
                    .Where(n => n != null)
                    .Select(n => n.Pt)
                    .Distinct()
                    .ToList();

                if (pts.Count < 4) return null;

                // Prefer RhinoCommon native 3D hull builder when available.
                // Use reflection so this still compiles against older RhinoCommon
                // versions that do not expose CreateConvexHull3D at compile time.
                var mi = typeof(Mesh).GetMethod(
                    "CreateConvexHull3D",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(IEnumerable<Point3d>),
                        typeof(int[][]).MakeByRefType(),
                        typeof(double),
                        typeof(double)
                    },
                    null);

                if (mi != null)
                {
                    object[] args = { pts, null, 1e-6, Math.PI / 180.0 };
                    var mesh = mi.Invoke(null, args) as Mesh;
                    if (mesh != null && mesh.IsValid && mesh.IsClosed)
                    {
                        mesh.Normals.ComputeNormals();
                        mesh.Compact();
                        return mesh;
                    }
                }

                // Fallback for environments where CreateConvexHull3D is unavailable:
                // no robust 3D hull builder is exposed in the referenced API.
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>14 – ShrinkWrap volume with configurable detail ratio.</summary>
        public static double ShrinkWrapVolume(SG_Shape shape, double detailRatio = DefaultShrinkWrapDetailRatio)
        {
            return ShrinkWrapVolume(shape, detailRatio, fastMode: false);
        }

        /// <summary>
        /// 14 – ShrinkWrap volume.  When <paramref name="fastMode"/> is <c>true</c> the
        /// internal mesh is built with very coarse parameters (no smoothing, no polygon
        /// optimisation, ≤2 samples per beam, edge length ≥ 0.10×diagonal) so that the
        /// metric is fast enough for GA inner-loop use (1000 pop × 100 gen = 100 000 calls).
        /// Absolute volume is slightly less precise but the relative ordering between
        /// shapes — which is all clustering needs — is preserved.
        /// </summary>
        public static double ShrinkWrapVolume(SG_Shape shape, double detailRatio, bool fastMode)
        {
            var m = ShrinkWrapMesh(shape, detailRatio, fastMode);
            if (m == null || !m.IsValid) return 0.0;
            try
            {
                var vp = VolumeMassProperties.Compute(m);
                return Math.Abs(vp?.Volume ?? 0.0);
            }
            catch { return 0.0; }
        }

        public static Mesh ShrinkWrapMesh(SG_Shape shape, double detailRatio = DefaultShrinkWrapDetailRatio)
        {
            return ShrinkWrapMesh(shape, detailRatio, fastMode: false);
        }

        /// <summary>
        /// Builds the ShrinkWrap mesh.  In <paramref name="fastMode"/>:
        ///  • TargetEdgeLength is clamped to ≥ 0.10×diagonal (≈25× fewer polys vs. detail 0.02).
        ///  • SmoothingIterations = 0, PolygonOptimization = 0, FillHolesInInputObjects = false.
        ///  • Beam sampling capped to ≤ 2 mid-points per element so the input cloud stays small.
        /// In normal mode the original high-quality settings are used (slow but accurate).
        /// </summary>
        public static Mesh ShrinkWrapMesh(SG_Shape shape, double detailRatio, bool fastMode)
        {
            if (shape?.Nodes == null || shape.Nodes.Count < 4) return null;

            // Fast mode forces a coarse target edge regardless of the user-supplied detail ratio.
            // We still let larger user values take effect (the user explicitly asked for coarser).
            double effectiveRatio = fastMode
                ? Math.Max(detailRatio, 0.10)
                : detailRatio;
            effectiveRatio = Math.Clamp(effectiveRatio, 0.001, 0.5);

            var bb = BoundingBox.Empty;
            foreach (var n in shape.Nodes)
            {
                if (n == null) continue;
                if (!bb.IsValid) bb = new BoundingBox(n.Pt, n.Pt);
                else bb.Union(n.Pt);
            }
            if (!bb.IsValid) return null;
            double diag = bb.Diagonal.Length;
            if (diag < 1e-9) return null;

            double targetEdge = Math.Max(1e-6, diag * effectiveRatio);
            var pc = new PointCloud();
            foreach (var n in shape.Nodes)
            {
                if (n == null) continue;
                pc.Add(n.Pt);
            }

            int maxSamplesPerBeam = fastMode ? 2 : int.MaxValue;
            foreach (var e in shape.Elems.OfType<SG_Elem1D>())
            {
                if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null) continue;
                var ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);
                if (!ln.IsValid || ln.Length <= 1e-9) continue;
                int div = Math.Max(1, (int)Math.Ceiling(ln.Length / targetEdge));
                if (div > maxSamplesPerBeam + 1) div = maxSamplesPerBeam + 1;
                for (int i = 1; i < div; i++)
                    pc.Add(ln.PointAt((double)i / div));
            }
            if (pc.Count < 4) return null;

            var p = new ShrinkWrapParameters
            {
                TargetEdgeLength = targetEdge,
                Offset = 0.0,
                SmoothingIterations = fastMode ? 0 : 1,
                FillHolesInInputObjects = !fastMode,
                PolygonOptimization = fastMode ? 0 : 20
            };
            var m = Mesh.ShrinkWrap(pc, p);
            if (m == null || !m.IsValid) return null;
            // Skip Normals.ComputeNormals + Compact in fast mode; volume calculation does not
            // need them and these passes are non-trivial for high poly counts.
            if (!fastMode)
            {
                m.Normals.ComputeNormals();
                m.Compact();
            }
            return m;
        }


        /// <summary>Computes area and mesh from line network via planar graph face extraction. Captures all enclosed cells (triangles, quads, etc.).</summary>
        public static (double area, Mesh mesh) MeshAreaFromLinesWithMesh(SG_Shape shape)
        {
            if (shape?.Elems == null || shape.Elems.Count == 0)
                return (0.0, null);

            const double tol = 1e-8;
            const double tolSq = tol * tol;

            if (!TryGetStructuralFootprintPlane(shape, out Plane footprint) || !footprint.IsValid)
                return (0.0, null);

            var segs = new List<(Point2d a, Point2d b)>();
            foreach (var elem in shape.Elems)
            {
                if (!(elem is SG_Elem1D e1) || e1.Nodes == null || e1.Nodes.Length < 2) continue;
                if (e1.Nodes[0] == null || e1.Nodes[1] == null) continue;
                footprint.ClosestParameter(e1.Nodes[0].Pt, out double ua, out double va);
                footprint.ClosestParameter(e1.Nodes[1].Pt, out double ub, out double vb);
                var a = new Point2d(ua, va);
                var b = new Point2d(ub, vb);
                if (DistSq2d(a, b) < tolSq) continue;
                segs.Add((a, b));
            }
            if (segs.Count == 0) return (0.0, null);

            var splitSegs = SplitSegmentsAtIntersections(segs, tol);
            if (splitSegs.Count == 0) return (0.0, null);

            var verts = new List<Point2d>(splitSegs.Count * 2);
            int GetVert(Point2d p)
            {
                for (int i = 0; i < verts.Count; i++)
                    if (DistSq2d(verts[i], p) <= tolSq) return i;
                verts.Add(p);
                return verts.Count - 1;
            }

            var edges = new List<(int from, int to)>();
            var edgeSet = new HashSet<(int, int)>();
            foreach (var (a, b) in splitSegs)
            {
                int u = GetVert(a), v = GetVert(b);
                if (u == v) continue;
                var uv = u < v ? (u, v) : (v, u);
                if (edgeSet.Contains(uv)) continue;
                edgeSet.Add(uv);
                edges.Add((u, v));
                edges.Add((v, u));
            }

            var adj = new List<List<int>>(verts.Count);
            for (int i = 0; i < verts.Count; i++) adj.Add(new List<int>());
            for (int e = 0; e < edges.Count; e++)
                adj[edges[e].from].Add(e);

            for (int v = 0; v < verts.Count; v++)
            {
                var list = adj[v];
                var pt = verts[v];
                list.Sort((ea, eb) =>
                {
                    var pa = verts[edges[ea].to];
                    var pb = verts[edges[eb].to];
                    return Math.Atan2(pa.Y - pt.Y, pa.X - pt.X).CompareTo(Math.Atan2(pb.Y - pt.Y, pb.X - pt.X));
                });
            }

            var nextEdge = new int[edges.Count];
            for (int e = 0; e < edges.Count; e++)
            {
                var list = adj[edges[e].to];
                int rev = e ^ 1;
                int idx = list.IndexOf(rev);
                nextEdge[e] = idx >= 0 ? list[(idx + 1) % list.Count] : -1;
            }

            var used = new bool[edges.Count];
            double totalArea = 0.0;
            var facePolys = new List<Point2d[]>();

            for (int start = 0; start < edges.Count; start++)
            {
                if (used[start]) continue;
                var face = new List<int>(64);
                int e = start;
                do
                {
                    face.Add(e);
                    used[e] = true;
                    e = nextEdge[e];
                } while (e >= 0 && e != start && face.Count < 10000);

                if (e != start || face.Count < 3) continue;
                var poly = new Point2d[face.Count];
                for (int i = 0; i < face.Count; i++)
                    poly[i] = verts[edges[face[i]].from];
                double area = ShoelaceSigned(poly);
                if (area > tol)
                {
                    totalArea += area;
                    facePolys.Add(poly);
                }
            }

            Mesh combined = null;
            if (facePolys.Count > 0)
            {
                combined = new Mesh();
                foreach (var poly in facePolys)
                {
                    var pts = new Point3d[poly.Length];
                    for (int i = 0; i < poly.Length; i++)
                    {
                        var p2 = poly[i];
                        pts[i] = footprint.PointAt(p2.X, p2.Y);
                    }
                    var pline = new Polyline(pts);
                    pline.Add(pts[0]);
                    var crv = pline.ToNurbsCurve();
                    if (crv != null && crv.IsClosed)
                    {
                        var breps = Brep.CreatePlanarBreps(crv, tol);
                        if (breps != null)
                            foreach (var brep in breps)
                            {
                                if (brep == null) continue;
                                var m = Mesh.CreateFromBrep(brep, MeshingParameters.Default);
                                if (m != null)
                                    foreach (var sub in m)
                                        if (sub != null && sub.IsValid) combined.Append(sub);
                            }
                    }
                }
            }
            return (totalArea, combined);
        }

        private static List<(Point2d a, Point2d b)> SplitSegmentsAtIntersections(List<(Point2d a, Point2d b)> segs, double tol)
        {
            var result = new List<(Point2d a, Point2d b)>();
            for (int i = 0; i < segs.Count; i++)
            {
                var (a, b) = segs[i];
                var pts = new List<double> { 0.0, 1.0 };
                double ax = b.X - a.X, ay = b.Y - a.Y;
                for (int j = 0; j < segs.Count; j++)
                {
                    if (i == j) continue;
                    var (c, d) = segs[j];
                    if (!SegSegIntersect(a, b, c, d, tol, out double t, out double u))
                        continue;
                    if (t > tol && t < 1 - tol) pts.Add(t);
                }
                pts.Sort();
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    double t0 = pts[k], t1 = pts[k + 1];
                    if (t1 - t0 < tol) continue;
                    var p0 = new Point2d(a.X + ax * t0, a.Y + ay * t0);
                    var p1 = new Point2d(a.X + ax * t1, a.Y + ay * t1);
                    if (DistSq2d(p0, p1) > tol * tol)
                        result.Add((p0, p1));
                }
            }
            return result;
        }

        private static bool SegSegIntersect(Point2d a, Point2d b, Point2d c, Point2d d, double tol, out double t, out double u)
        {
            t = u = 0;
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = d.X - c.X, wy = d.Y - c.Y;
            double det = vx * wy - vy * wx;
            if (Math.Abs(det) < tol * tol) return false;
            double cx = c.X - a.X, cy = c.Y - a.Y;
            t = (cx * wy - cy * wx) / det;
            u = (cx * vy - cy * vx) / det;
            return t >= -tol && t <= 1 + tol && u >= -tol && u <= 1 + tol;
        }

        private static double DistSq2d(Point2d a, Point2d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static double ShoelaceSigned(Point2d[] p)
        {
            if (p == null || p.Length < 3) return 0;
            double a = 0;
            for (int i = 0, n = p.Length; i < n; i++)
                a += p[i].X * (p[(i + 1) % n].Y - p[(i + n - 1) % n].Y);
            return a * 0.5;
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