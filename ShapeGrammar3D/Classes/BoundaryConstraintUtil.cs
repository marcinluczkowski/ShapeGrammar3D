using System;
using System.Collections.Generic;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Boundary constraint helpers used across rules and interpreters.
    /// Supports arbitrary closed Brep/Mesh boundaries (not just boxes).
    ///
    /// BoundaryBeamConstraintMode semantics:
    ///   0  : disabled (no check, no removal, no penalty).
    ///   1  : hard constraint — remove members that are mostly outside.
    ///   >=2: soft constraint — length-weighted outside ratio penalizes feasibility
    ///        with weight = mode value (penalty is intentionally NOT capped at 1).
    ///
    /// All inside/outside tests are tolerant: a point within
    /// <see cref="DefaultSurfaceTol"/> of the boundary surface counts as inside, so
    /// members lying on the boundary face are preserved.
    /// </summary>
    public static class BoundaryConstraintUtil
    {
        /// <summary>Default removal threshold: keep a member unless more than this
        /// fraction of its length is clearly outside.</summary>
        public const double DefaultRemovalThreshold = 0.5;

        /// <summary>Default sample count along a line for outside-ratio estimation.
        /// 9 is a good balance between accuracy and speed (n+1 = 10 samples, giving
        /// 10% resolution on the length-weighted outside ratio). The previous value
        /// of 21 was roughly 2x slower with negligible gain for a smooth gradient.</summary>
        public const int DefaultSampleCount = 9;

        /// <summary>
        /// Returns a tolerance ≈ 0.1% of the boundary's bounding-box diagonal. This
        /// works regardless of unit (mm or m) and makes "on-surface" members robust
        /// against floating-point inside/outside flips.
        /// </summary>
        public static double DefaultSurfaceTol(Brep brep, Mesh mesh)
        {
            BoundingBox bb = BoundingBox.Empty;
            if (brep != null) bb = brep.GetBoundingBox(true);
            else if (mesh != null) bb = mesh.GetBoundingBox(true);
            double diag = bb.IsValid ? bb.Diagonal.Length : 1.0;
            return Math.Max(diag * 0.001, 1e-6);
        }

        /// <summary>
        /// Returns the inflated bounding box used as a fast reject path for
        /// <see cref="IsPointInside"/>. Points outside it are trivially outside.
        /// </summary>
        private static BoundingBox GetInflatedBoundingBox(Brep brep, Mesh mesh, double surfaceTol)
        {
            BoundingBox bb = BoundingBox.Empty;
            if (brep != null) bb = brep.GetBoundingBox(true);
            else if (mesh != null) bb = mesh.GetBoundingBox(true);
            if (!bb.IsValid) return bb;
            bb.Inflate(surfaceTol);
            return bb;
        }

        /// <summary>
        /// Tolerant point-in-body test. A point is "inside" if it is strictly inside
        /// OR within <paramref name="surfaceTol"/> of the boundary surface.
        /// Returns true when no boundary is supplied.
        /// </summary>
        public static bool IsPointInside(Point3d pt, Brep brep, Mesh mesh, double surfaceTol = 0.0)
        {
            if (brep == null && mesh == null) return true;
            if (surfaceTol <= 0) surfaceTol = DefaultSurfaceTol(brep, mesh);

            // Fast reject: points clearly outside the (inflated) bounding box can
            // never be inside. Saves a very expensive Brep.IsPointInside / ClosestPoint
            // call for every out-of-BBox sample - which dominates runtime when a
            // large fraction of each line lies outside.
            var bb = GetInflatedBoundingBox(brep, mesh, surfaceTol);
            if (bb.IsValid && !bb.Contains(pt, true)) return false;

            if (brep != null)
            {
                if (brep.IsPointInside(pt, surfaceTol, false)) return true;
                if (brep.ClosestPoint(pt, out Point3d cp, out _, out _, out _, surfaceTol, out _))
                    if (pt.DistanceTo(cp) <= surfaceTol) return true;
                return false;
            }

            if (mesh.IsPointInside(pt, surfaceTol, false)) return true;
            Point3d cpM = mesh.ClosestPoint(pt);
            return cpM.IsValid && cpM.DistanceTo(pt) <= surfaceTol;
        }

        /// <summary>Returns the fraction (0..1) of <paramref name="ln"/> lying outside
        /// the boundary, based on uniform sampling.</summary>
        public static double OutsideRatio(Line ln, Brep brep, Mesh mesh,
            double surfaceTol = 0.0, int nSamples = DefaultSampleCount)
        {
            if (brep == null && mesh == null) return 0.0;
            if (nSamples < 3) nSamples = 3;
            if (surfaceTol <= 0) surfaceTol = DefaultSurfaceTol(brep, mesh);

            var bb = GetInflatedBoundingBox(brep, mesh, surfaceTol);

            int outside = 0;
            int total = nSamples + 1;
            for (int i = 0; i <= nSamples; i++)
            {
                double t = (double)i / nSamples;
                Point3d p = ln.PointAt(t);

                // Inline fast reject using the cached BB so we pay it once per call.
                if (bb.IsValid && !bb.Contains(p, true)) { outside++; continue; }
                if (!IsPointInside(p, brep, mesh, surfaceTol)) outside++;
            }
            return (double)outside / total;
        }

        /// <summary>True when more than <paramref name="thresholdRatio"/> (0..1) of
        /// the line lies outside. Members on/near the surface evaluate to false.</summary>
        public static bool IsLineOutsideBoundary(Line ln, Brep brep, Mesh mesh,
            double surfaceTol = 0.0,
            double thresholdRatio = DefaultRemovalThreshold,
            int nSamples = DefaultSampleCount)
        {
            return OutsideRatio(ln, brep, mesh, surfaceTol, nSamples) > thresholdRatio;
        }

        /// <summary>
        /// Length-weighted outside ratio: Σ(len_i · outsideRatio_i) / Σlen_i, in 0..1.
        /// Used by FeasibilityMetrics for soft-constraint penalty (smooth gradient).
        /// </summary>
        public static double ComputeLengthWeightedOutsideRatio(SG_Shape shape, Brep brep, Mesh mesh,
            double surfaceTol = 0.0)
        {
            if (shape?.Elems == null) return 0.0;
            if (brep == null && mesh == null) return 0.0;
            if (surfaceTol <= 0) surfaceTol = DefaultSurfaceTol(brep, mesh);

            double totalLen = 0.0;
            double outsideLen = 0.0;

            foreach (var el in shape.Elems)
            {
                if (el is not SG_Elem1D e) continue;
                if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null) continue;
                Line ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);
                if (!ln.IsValid || ln.Length < 1e-9) continue;

                double r = OutsideRatio(ln, brep, mesh, surfaceTol);
                totalLen += ln.Length;
                outsideLen += ln.Length * r;
            }

            return totalLen > 1e-12 ? outsideLen / totalLen : 0.0;
        }

        /// <summary>
        /// Enforces the boundary constraint on a shape.
        /// </summary>
        /// <returns>Number of elements removed (only non-zero for mode == 1).</returns>
        public static int Enforce(SG_Shape shape, Brep brep, Mesh mesh, int mode,
            double removalThreshold = DefaultRemovalThreshold)
        {
            if (shape?.Elems == null) return 0;
            if (brep == null && mesh == null)
            {
                shape.BoundaryViolationRatio = 0.0;
                shape.BoundaryViolationWeight = 0.0;
                return 0;
            }

            double surfaceTol = DefaultSurfaceTol(brep, mesh);
            var hardRemove = new List<SG_Element>();
            double totalLen = 0.0;
            double outsideLen = 0.0;
            double keptTotalLen = 0.0;
            double keptOutsideLen = 0.0;

            foreach (var el in shape.Elems)
            {
                if (el is not SG_Elem1D e) continue;
                if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null) continue;
                Line ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);
                if (!ln.IsValid || ln.Length < 1e-9) continue;

                double r = OutsideRatio(ln, brep, mesh, surfaceTol);
                double len = ln.Length;
                totalLen += len;
                outsideLen += len * r;

                // Members on the surface have r ≈ 0 → kept. Only majority-outside
                // members are removed in hard-constraint mode.
                bool remove = mode == 1 && r > removalThreshold;
                if (remove)
                {
                    hardRemove.Add(el);
                }
                else
                {
                    keptTotalLen += len;
                    keptOutsideLen += len * r;
                }
            }

            if (mode == 1 && hardRemove.Count > 0)
            {
                foreach (var el in hardRemove)
                {
                    if (el is SG_Elem1D e2 && e2.Nodes != null)
                        foreach (var n in e2.Nodes)
                            n?.Elements?.Remove(el);
                    shape.Elems.Remove(el);
                }
                shape.RemoveUnusedNodes();
            }

            // Single-pass: the residual ratio over what remains was accumulated
            // while we were deciding what to keep, so no second expensive scan.
            double finalRatio;
            if (mode == 1)
                finalRatio = keptTotalLen > 1e-12 ? keptOutsideLen / keptTotalLen : 0.0;
            else
                finalRatio = totalLen > 1e-12 ? outsideLen / totalLen : 0.0;

            shape.BoundaryViolationRatio = finalRatio;
            shape.BoundaryViolationWeight = mode >= 2 ? mode : 0.0;

            return mode == 1 ? hardRemove.Count : 0;
        }
    }
}
