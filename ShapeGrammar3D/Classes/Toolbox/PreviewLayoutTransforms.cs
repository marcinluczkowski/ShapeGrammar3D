using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes.Toolbox
{
    /// <summary>
    /// Shared helpers for the GI_* preview components that lay out structures in a
    /// (column, row) grid. The convention used everywhere:
    ///
    ///  * Column Spacing and Row Spacing are <see cref="Vector3d"/> values, applied
    ///    directly in world coordinates. This lets the user choose both magnitude
    ///    and direction (e.g. (0, 0, 50) makes columns spread along world Z).
    ///  * The optional Display Plane defines the orientation each cell's local
    ///    XY plane is mapped to. When not connected the components fall back to
    ///    <c>Plane(Origin, XAxis, ZAxis)</c> – the world XZ plane – so previews
    ///    stand up in a regular Rhino front view by default.
    ///  * Per-cell orientation is applied around each cell's own origin, which is
    ///    why cell positions stay exactly at <c>insertPt + col*Col + row*Row</c>
    ///    in world space regardless of the display plane.
    /// </summary>
    public static class PreviewLayoutTransforms
    {
        /// <summary>Default column step used when registering Column Spacing inputs (30 along world X).</summary>
        public static readonly Vector3d DefaultColumnSpacing = new Vector3d(30.0, 0.0, 0.0);

        /// <summary>Compact preview default row step (-10 along world Z), used by the metrics-style components.</summary>
        public static readonly Vector3d DefaultRowSpacingCompact = new Vector3d(0.0, 0.0, -10.0);

        /// <summary>Wide preview default row step (-30 along world Z), used by the structure-preview components.</summary>
        public static readonly Vector3d DefaultRowSpacingWide = new Vector3d(0.0, 0.0, -30.0);

        /// <summary>
        /// World XZ plane at the origin – the unified default Display Plane.
        /// </summary>
        public static Plane DefaultDisplayPlane =>
            new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis);

        /// <summary>
        /// Reads the Display Plane input. Falls back to the world XZ plane when the
        /// input is disconnected or invalid so all previews share a sensible default.
        /// </summary>
        public static Plane GetOptionalDisplayPlane(IGH_DataAccess DA, int planeParamIndex)
        {
            Plane userPlane = default;
            if (DA.GetData(planeParamIndex, ref userPlane) && userPlane.IsValid)
                return userPlane;
            return DefaultDisplayPlane;
        }

        /// <summary>
        /// Builds a per-cell orient transform that rotates a cell built in world XY
        /// (around <paramref name="cellOrigin"/>) so that its X/Y axes align with
        /// the supplied <paramref name="displayPlane"/>'s X/Y axes. The plane's
        /// origin is intentionally ignored – the rotation pivot is the cell origin
        /// so cell positions stay exactly where the spacing vectors place them.
        /// </summary>
        public static Transform GetCellOrientTransform(Plane displayPlane, Point3d cellOrigin)
        {
            if (!displayPlane.IsValid) return Transform.Identity;
            Plane from = new Plane(cellOrigin, Vector3d.XAxis, Vector3d.YAxis);
            Plane to = new Plane(cellOrigin, displayPlane.XAxis, displayPlane.YAxis);
            // Skip the PlaneToPlane call entirely when the display plane already
            // matches world XY – pure-translation rotation is wasted work and can
            // introduce tiny numeric drift on identity transforms.
            if (from.XAxis.EpsilonEquals(to.XAxis, 1e-12)
                && from.YAxis.EpsilonEquals(to.YAxis, 1e-12))
                return Transform.Identity;
            return Transform.PlaneToPlane(from, to);
        }

        /// <summary>
        /// Orient transform for genuine 3D structure geometry (authored in world
        /// coordinates with its main span along X and its height along Z). Maps the
        /// content's world XZ frame (X = main direction, Z = up) onto the display
        /// plane's X/Y axes, around <paramref name="cellOrigin"/>.
        ///
        /// This differs from <see cref="GetCellOrientTransform"/>, which assumes flat
        /// content authored in world XY (used by the radar/table previews). For the
        /// default world XZ display plane this returns the identity transform, so 3D
        /// structures keep their authored orientation (main direction along X, height
        /// along Z) instead of being tipped 90° onto their side.
        /// </summary>
        public static Transform GetCellOrientTransform3D(Plane displayPlane, Point3d cellOrigin)
        {
            if (!displayPlane.IsValid) return Transform.Identity;
            Plane from = new Plane(cellOrigin, Vector3d.XAxis, Vector3d.ZAxis);
            Plane to = new Plane(cellOrigin, displayPlane.XAxis, displayPlane.YAxis);
            if (from.XAxis.EpsilonEquals(to.XAxis, 1e-12)
                && from.YAxis.EpsilonEquals(to.YAxis, 1e-12))
                return Transform.Identity;
            return Transform.PlaneToPlane(from, to);
        }

        /// <summary>
        /// Legacy helper kept so individual components can still be migrated one at
        /// a time. Returns the transform that maps the world-XY layout plane (at
        /// <paramref name="layoutOrigin"/>) onto the user-supplied plane, defaulting
        /// to the world XZ plane through <paramref name="layoutOrigin"/>.
        /// </summary>
        public static Transform GetOptionalDisplayTransform(IGH_DataAccess DA, int planeParamIndex,
            Point3d layoutOrigin)
        {
            Plane userPlane = default;
            bool got = DA.GetData(planeParamIndex, ref userPlane);
            Plane targetPlane;
            if (got && userPlane.IsValid)
            {
                targetPlane = userPlane;
            }
            else
            {
                // Default: stand the preview up on the world XZ plane through layoutOrigin.
                targetPlane = new Plane(layoutOrigin, Vector3d.XAxis, Vector3d.ZAxis);
            }
            var layoutPlane = new Plane(layoutOrigin, Vector3d.XAxis, Vector3d.YAxis);
            if (layoutPlane.XAxis.EpsilonEquals(targetPlane.XAxis, 1e-12)
                && layoutPlane.YAxis.EpsilonEquals(targetPlane.YAxis, 1e-12)
                && layoutPlane.Origin.EpsilonEquals(targetPlane.Origin, 1e-12))
                return Transform.Identity;
            return Transform.PlaneToPlane(layoutPlane, targetPlane);
        }

        public static GH_Structure<GH_Line> TransformLineTree(GH_Structure<GH_Line> src, Transform xf)
        {
            if (xf.IsIdentity) return src;
            var dst = new GH_Structure<GH_Line>();
            foreach (var path in src.Paths)
            {
                foreach (var goo in src.get_Branch(path))
                {
                    if (goo is GH_Line gh)
                    {
                        var ln = gh.Value;
                        ln.Transform(xf);
                        dst.Append(new GH_Line(ln), path);
                    }
                }
            }
            return dst;
        }

        public static GH_Structure<GH_Curve> TransformCurveTree(GH_Structure<GH_Curve> src, Transform xf)
        {
            if (xf.IsIdentity) return src;
            var dst = new GH_Structure<GH_Curve>();
            foreach (var path in src.Paths)
            {
                foreach (var goo in src.get_Branch(path))
                {
                    if (goo is GH_Curve gc && gc.Value != null)
                    {
                        var dup = gc.Value.DuplicateCurve();
                        dup.Transform(xf);
                        dst.Append(new GH_Curve(dup), path);
                    }
                }
            }
            return dst;
        }

        public static GH_Structure<GH_Mesh> TransformMeshTree(GH_Structure<GH_Mesh> src, Transform xf)
        {
            if (xf.IsIdentity) return src;
            var dst = new GH_Structure<GH_Mesh>();
            foreach (var path in src.Paths)
            {
                foreach (var goo in src.get_Branch(path))
                {
                    if (goo is GH_Mesh gm && gm.Value != null)
                    {
                        var dup = gm.Value.DuplicateMesh();
                        dup.Transform(xf);
                        dst.Append(new GH_Mesh(dup), path);
                    }
                }
            }
            return dst;
        }

        public static GH_Structure<GH_Point> TransformPointTree(GH_Structure<GH_Point> src, Transform xf)
        {
            if (xf.IsIdentity) return src;
            var dst = new GH_Structure<GH_Point>();
            foreach (var path in src.Paths)
            {
                foreach (var goo in src.get_Branch(path))
                {
                    if (goo is GH_Point gp)
                    {
                        var p = gp.Value;
                        p.Transform(xf);
                        dst.Append(new GH_Point(p), path);
                    }
                }
            }
            return dst;
        }

        public static GH_Structure<GH_Vector> TransformVectorTree(GH_Structure<GH_Vector> src, Transform xf)
        {
            if (xf.IsIdentity) return src;
            var dst = new GH_Structure<GH_Vector>();
            foreach (var path in src.Paths)
            {
                foreach (var goo in src.get_Branch(path))
                {
                    if (goo is GH_Vector gv)
                    {
                        var v = gv.Value;
                        v.Transform(xf);
                        dst.Append(new GH_Vector(v), path);
                    }
                }
            }
            return dst;
        }
    }
}
