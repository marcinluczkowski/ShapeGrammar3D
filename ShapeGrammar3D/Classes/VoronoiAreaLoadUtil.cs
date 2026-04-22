using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Shared helpers that choose Voronoi seed/load points for area-load distribution.
    /// For RULE020 struts, the endpoint closer to the roof boundary is used.
    /// </summary>
    public static class VoronoiAreaLoadUtil
    {
        /// <summary>
        /// Vertical distance from a point to the "roof" surface of the boundary.
        /// The roof is defined as the boundary surface found by ray-casting from
        /// <paramref name="pt"/> along the +Z axis (and -Z as a fall-through when
        /// the point sits above the boundary). This avoids treating the floor of
        /// a closed BoundaryBrep/Mesh as the "roof" when a strut's foot happens
        /// to touch the floor.
        ///
        /// Falls back to <c>|fallbackBb.Max.Z - pt.Z|</c> when the boundary is
        /// missing or the rays don't hit anything.
        /// </summary>
        public static double DistanceToRoofSurface(Point3d pt, SG_Shape shape, BoundingBox fallbackBb)
        {
            double span = System.Math.Max(1e3, fallbackBb.Diagonal.Length * 10.0);

            double upHit = CastForRoofHit(pt, +1.0, span, shape);
            if (upHit >= 0.0) return upHit;

            double downHit = CastForRoofHit(pt, -1.0, span, shape);
            if (downHit >= 0.0) return downHit;

            double zTop = fallbackBb.Max.Z;
            return System.Math.Abs(zTop - pt.Z);
        }

        /// <summary>
        /// Casts a vertical ray from <paramref name="pt"/> in the given
        /// <paramref name="zDir"/> direction (±1) against the shape's
        /// BoundaryBrep / BoundaryMesh and returns the smallest positive
        /// travel distance, or <c>-1</c> when no intersection is found.
        /// </summary>
        private static double CastForRoofHit(Point3d pt, double zDir, double span, SG_Shape shape)
        {
            var end = pt + new Vector3d(0, 0, zDir) * span;
            var line = new Line(pt, end);
            if (!line.IsValid || line.Length <= 1e-9) return -1.0;

            double best = double.PositiveInfinity;

            if (shape?.BoundaryBrep != null && shape.BoundaryBrep.IsValid)
            {
                var crv = line.ToNurbsCurve();
                if (crv != null && Intersection.CurveBrep(crv, shape.BoundaryBrep,
                        0.001, out _, out Point3d[] ipts) && ipts != null)
                {
                    foreach (var ip in ipts)
                    {
                        double d = pt.DistanceTo(ip);
                        if (d > 1e-6 && d < best) best = d;
                    }
                }
            }

            if (shape?.BoundaryMesh != null && shape.BoundaryMesh.IsValid)
            {
                var mpts = Intersection.MeshLine(shape.BoundaryMesh, line, out _);
                if (mpts != null)
                {
                    foreach (var ip in mpts)
                    {
                        double d = pt.DistanceTo(ip);
                        if (d > 1e-6 && d < best) best = d;
                    }
                }
            }

            return double.IsPositiveInfinity(best) ? -1.0 : best;
        }

        /// <summary>
        /// For every RULE020 strut, pick the endpoint closer to the roof boundary.
        /// Returns the picked nodes as a distinct (ID-deduplicated) ordered list.
        /// Roof-hit distances are cached per node ID so struts sharing an endpoint
        /// avoid redundant ray-casts against the Brep/Mesh boundary.
        /// </summary>
        public static List<SG_Node> CollectStrutRoofNearerNodes(SG_Shape shape, BoundingBox fallbackBb)
        {
            var list = new List<SG_Node>();
            var seen = new HashSet<int>();
            if (shape?.Elems == null) return list;

            var distCache = new Dictionary<int, double>();

            foreach (var el in shape.Elems)
            {
                if (el is not SG_Elem1D e) continue;
                if (e.Autorule != UT.RULE020_MARKER) continue;
                if (e.Nodes == null || e.Nodes.Length < 2) continue;
                SG_Node n0 = e.Nodes[0];
                SG_Node n1 = e.Nodes[1];
                if (n0 == null || n1 == null) continue;

                if (!distCache.TryGetValue(n0.ID, out double d0))
                {
                    d0 = DistanceToRoofSurface(n0.Pt, shape, fallbackBb);
                    distCache[n0.ID] = d0;
                }
                if (!distCache.TryGetValue(n1.ID, out double d1))
                {
                    d1 = DistanceToRoofSurface(n1.Pt, shape, fallbackBb);
                    distCache[n1.ID] = d1;
                }
                SG_Node chosen = d0 <= d1 ? n0 : n1;
                if (!seen.Add(chosen.ID)) continue;
                list.Add(chosen);
            }

            return list;
        }

        /// <summary>
        /// Distinct endpoint nodes of every 1D element. Used as Voronoi seeds when
        /// RULE020 struts do not yet exist (e.g. initial-shape stage).
        /// </summary>
        public static List<SG_Node> CollectAllElem1DEndpointNodes(SG_Shape shape)
        {
            var map = new Dictionary<int, SG_Node>();
            if (shape?.Elems == null) return new List<SG_Node>();
            foreach (var e in shape.Elems.OfType<SG_Elem1D>())
            {
                if (e?.Nodes == null) continue;
                foreach (var n in e.Nodes)
                {
                    if (n == null) continue;
                    map[n.ID] = n;
                }
            }
            return map.Values.ToList();
        }

        /// <summary>
        /// Voronoi seeds for area-load distribution. Uses
        /// <see cref="CollectStrutRoofNearerNodes"/> when RULE020 struts exist,
        /// otherwise falls back to <see cref="CollectAllElem1DEndpointNodes"/> so that
        /// InitShape (beams only) and the final TB preprocessing stay consistent.
        /// </summary>
        public static List<SG_Node> CollectAreaLoadVoronoiSeedNodes(SG_Shape shape, BoundingBox fallbackBb)
        {
            var strutSeeds = CollectStrutRoofNearerNodes(shape, fallbackBb);
            if (strutSeeds.Count > 0) return strutSeeds;
            return CollectAllElem1DEndpointNodes(shape);
        }
    }
}
