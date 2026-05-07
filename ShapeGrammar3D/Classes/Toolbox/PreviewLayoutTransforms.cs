using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes.Toolbox
{
    /// <summary>
    /// Optional orient for preview components that lay out grids in a world-XY–style
    /// plane through <paramref name="layoutOrigin"/> (X/Y offsets, Z = layoutOrigin.Z).
    /// Maps that plane onto a user <c>Plane</c> input via <see cref="Transform.PlaneToPlane"/>.
    /// When the input is not connected, returns identity (legacy behaviour).
    /// </summary>
    public static class PreviewLayoutTransforms
    {
        public static Transform GetOptionalDisplayTransform(IGH_DataAccess DA, int planeParamIndex,
            Point3d layoutOrigin)
        {
            Plane userPlane = default;
            if (!DA.GetData(planeParamIndex, ref userPlane))
                return Transform.Identity;
            if (!userPlane.IsValid)
                return Transform.Identity;
            var layoutPlane = new Plane(layoutOrigin, Vector3d.XAxis, Vector3d.YAxis);
            return Transform.PlaneToPlane(layoutPlane, userPlane);
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
