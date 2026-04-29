using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    ///
    /// Performance: a per-Brep cache (<see cref="BoundaryContext"/>) is kept across
    /// calls so the inflated bounding box, surface tolerance, and (crucially)
    /// the Brep → Mesh conversion happen exactly once per unique boundary.
    /// Inside-tests then run on the cached mesh, which is typically 5–15× faster
    /// than <c>Brep.IsPointInside</c> for the same inputs.  In a 1000-individual
    /// GA loop with 600 elements and 10 samples per element this turns ≈1 M Brep
    /// calls into ≈1 M Mesh calls — the dominant cost of <c>Enforce</c> in the
    /// previous implementation.
    /// </summary>
    public static class BoundaryConstraintUtil
    {
        /// <summary>
        /// Optional profiling hook.  When non-null, BoundaryConstraintUtil accumulates
        /// wall-clock timings into this dictionary using keys prefixed with
        /// "Boundary[...]".  Leave null for normal use.
        /// </summary>
        public static Dictionary<string, double> ProfileMsAccumulator;

        /// <summary>Default removal threshold: keep a member unless more than this
        /// fraction of its length is clearly outside.</summary>
        public const double DefaultRemovalThreshold = 0.5;

        /// <summary>Default sample count along a line for outside-ratio estimation.
        /// 5 samples (n=4 → n+1=5 points) gives 20 % resolution on the length-weighted
        /// outside ratio, which is sufficient for the smooth feasibility gradient and
        /// roughly half the cost of the previous default of 9.  Increase the explicit
        /// argument to <see cref="OutsideRatio"/> when you need finer resolution.</summary>
        public const int DefaultSampleCount = 4;

        // ------------------------------------------------------------------
        // Per-boundary context cache
        // ------------------------------------------------------------------

        /// <summary>
        /// Caches everything that is expensive to compute from a (Brep, Mesh) pair so
        /// we don't redo it per element / per sample / per individual.  Built lazily
        /// the first time a boundary is seen and reused while the same (Brep, Mesh)
        /// references keep being passed in (i.e. the entire GA loop).
        /// </summary>
        public sealed class BoundaryContext
        {
            public Brep Brep { get; }
            public Mesh Mesh { get; }
            public BoundingBox InflatedBBox { get; }
            public double SurfaceTol { get; }

            /// <summary>
            /// A mesh suitable for fast inside tests.  Equal to <see cref="Mesh"/> when
            /// the user supplied one, otherwise derived from <see cref="Brep"/> via
            /// <c>Mesh.CreateFromBrep</c>.  Null when both inputs are null OR when the
            /// Brep meshing failed (fallback path uses Brep.IsPointInside).
            /// </summary>
            public Mesh InsideTestMesh { get; }

            internal BoundaryContext(Brep brep, Mesh mesh)
            {
                Brep = brep;
                Mesh = mesh;

                // Bounding box from whichever input is available.
                BoundingBox bb = BoundingBox.Empty;
                if (brep != null) bb = brep.GetBoundingBox(true);
                else if (mesh != null) bb = mesh.GetBoundingBox(true);

                double diag = bb.IsValid ? bb.Diagonal.Length : 1.0;
                SurfaceTol = Math.Max(diag * 0.001, 1e-6);

                if (bb.IsValid) bb.Inflate(SurfaceTol);
                InflatedBBox = bb;

                // Prefer an explicit user mesh.  Otherwise derive a mesh from the Brep
                // ONCE and reuse it for every inside test from now on — this is the
                // single biggest speed-up because Mesh.IsPointInside is dramatically
                // faster than Brep.IsPointInside for repeated queries.
                if (mesh != null)
                {
                    InsideTestMesh = mesh;
                }
                else if (brep != null)
                {
                    InsideTestMesh = TryMeshBrep(brep);
                }
            }

            private static Mesh TryMeshBrep(Brep brep)
            {
                try
                {
                    Stopwatch sw = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;

                    // Default meshing parameters: a balance between accuracy and speed.
                    // We do not need a render-quality mesh — only correct topology so
                    // the closed-mesh inside test works.  The surfaceTol slack absorbs
                    // any slight deviation between the mesh and the Brep surface.
                    var mp = MeshingParameters.Default;
                    var pieces = Mesh.CreateFromBrep(brep, mp);
                    if (pieces == null || pieces.Length == 0) return null;

                    var combined = new Mesh();
                    foreach (var m in pieces)
                        if (m != null) combined.Append(m);

                    if (!combined.IsValid) return null;
                    combined.FaceNormals.ComputeFaceNormals();
                    combined.Normals.ComputeNormals();
                    combined.Compact();
                    Accum(sw, "Boundary[BrepToMesh]");
                    return combined;
                }
                catch
                {
                    return null;
                }
            }
        }

        // Single-slot cache: identity check on (Brep, Mesh).  Sufficient because the
        // GA loop passes the SAME pair every iteration; rebuilding only happens if
        // the user actually changes the boundary input on the canvas.
        private static BoundaryContext _lastContext;

        /// <summary>
        /// Returns a cached <see cref="BoundaryContext"/> for the given inputs, or
        /// builds a fresh one if the inputs differ from the last call.  Pass the
        /// returned context to the overloads below to avoid recomputing the bbox,
        /// surface tolerance, and Brep-derived mesh on every call.
        /// </summary>
        public static BoundaryContext GetOrCreateContext(Brep brep, Mesh mesh)
        {
            var cached = _lastContext;
            if (cached != null
                && ReferenceEquals(cached.Brep, brep)
                && ReferenceEquals(cached.Mesh, mesh))
                return cached;

            Stopwatch sw = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;
            var fresh = new BoundaryContext(brep, mesh);
            Accum(sw, "Boundary[ContextBuild]");
            _lastContext = fresh;
            return fresh;
        }

        /// <summary>Drops the cached <see cref="BoundaryContext"/>.  Useful from tests
        /// or when the underlying Brep/Mesh content has been mutated in-place.</summary>
        public static void ClearContextCache() => _lastContext = null;

        // ------------------------------------------------------------------
        // Public API (backward compatible)
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a tolerance ≈ 0.1% of the boundary's bounding-box diagonal. This
        /// works regardless of unit (mm or m) and makes "on-surface" members robust
        /// against floating-point inside/outside flips.
        /// </summary>
        public static double DefaultSurfaceTol(Brep brep, Mesh mesh)
            => GetOrCreateContext(brep, mesh).SurfaceTol;

        /// <summary>
        /// Tolerant point-in-body test. A point is "inside" if it is strictly inside
        /// OR within <paramref name="surfaceTol"/> of the boundary surface.
        /// Returns true when no boundary is supplied.
        /// </summary>
        public static bool IsPointInside(Point3d pt, Brep brep, Mesh mesh, double surfaceTol = 0.0)
        {
            if (brep == null && mesh == null) return true;
            var ctx = GetOrCreateContext(brep, mesh);
            double tol = surfaceTol > 0 ? surfaceTol : ctx.SurfaceTol;
            return IsPointInside(pt, ctx, tol);
        }

        /// <summary>Fast path: takes a precomputed context so the bbox, surfaceTol,
        /// and (Brep-derived) mesh do not get rebuilt on every call.</summary>
        public static bool IsPointInside(Point3d pt, BoundaryContext ctx, double surfaceTol)
        {
            if (ctx == null) return true;
            if (ctx.Brep == null && ctx.Mesh == null && ctx.InsideTestMesh == null) return true;

            // Fast reject: clearly outside the inflated bounding box ⇒ trivially outside.
            if (ctx.InflatedBBox.IsValid && !ctx.InflatedBBox.Contains(pt, true))
                return false;

            // Preferred: mesh-based inside test (5–15× faster than Brep equivalents
            // and equally robust at the surface-tolerance scale we work at).
            if (ctx.InsideTestMesh != null)
            {
                Stopwatch swInside = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;
                bool inside = ctx.InsideTestMesh.IsPointInside(pt, surfaceTol, false);
                Accum(swInside, "Boundary[Mesh.IsPointInside]");
                if (inside) return true;

                Stopwatch swClosest = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;
                Point3d cpM = ctx.InsideTestMesh.ClosestPoint(pt);
                Accum(swClosest, "Boundary[Mesh.ClosestPoint]");
                return cpM.IsValid && cpM.DistanceTo(pt) <= surfaceTol;
            }

            // Fallback: original Brep API.  Only reached when both Mesh inputs are
            // missing AND the Brep failed to mesh — rare, but kept correct.
            if (ctx.Brep != null)
            {
                Stopwatch swInside = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;
                bool inside = ctx.Brep.IsPointInside(pt, surfaceTol, false);
                Accum(swInside, "Boundary[Brep.IsPointInside]");
                if (inside) return true;

                Stopwatch swClosest = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;
                if (ctx.Brep.ClosestPoint(pt, out Point3d cp, out _, out _, out _, surfaceTol, out _))
                {
                    Accum(swClosest, "Boundary[Brep.ClosestPoint]");
                    if (pt.DistanceTo(cp) <= surfaceTol) return true;
                }
                else
                {
                    Accum(swClosest, "Boundary[Brep.ClosestPoint]");
                }
                return false;
            }

            return true;
        }

        /// <summary>Returns the fraction (0..1) of <paramref name="ln"/> lying outside
        /// the boundary, based on uniform sampling.</summary>
        public static double OutsideRatio(Line ln, Brep brep, Mesh mesh,
            double surfaceTol = 0.0, int nSamples = DefaultSampleCount)
        {
            if (brep == null && mesh == null) return 0.0;
            var ctx = GetOrCreateContext(brep, mesh);
            double tol = surfaceTol > 0 ? surfaceTol : ctx.SurfaceTol;
            return OutsideRatio(ln, ctx, tol, nSamples);
        }

        /// <summary>Fast path: takes a precomputed context.</summary>
        public static double OutsideRatio(Line ln, BoundaryContext ctx, double surfaceTol,
            int nSamples = DefaultSampleCount)
        {
            if (ctx == null) return 0.0;
            if (ctx.Brep == null && ctx.Mesh == null && ctx.InsideTestMesh == null) return 0.0;
            if (nSamples < 3) nSamples = 3;

            int outside = 0;
            int total = nSamples + 1;
            BoundingBox bb = ctx.InflatedBBox;

            for (int i = 0; i <= nSamples; i++)
            {
                double t = (double)i / nSamples;
                Point3d p = ln.PointAt(t);

                // Inline fast reject using the cached BB so we pay it once per sample.
                if (bb.IsValid && !bb.Contains(p, true)) { outside++; continue; }
                if (!IsPointInside(p, ctx, surfaceTol)) outside++;
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
            var ctx = GetOrCreateContext(brep, mesh);
            double tol = surfaceTol > 0 ? surfaceTol : ctx.SurfaceTol;

            double totalLen = 0.0;
            double outsideLen = 0.0;

            foreach (var el in shape.Elems)
            {
                if (el is not SG_Elem1D e) continue;
                if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null) continue;
                Line ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);
                if (!ln.IsValid || ln.Length < 1e-9) continue;

                double r = OutsideRatio(ln, ctx, tol);
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

            var ctx = GetOrCreateContext(brep, mesh);
            double surfaceTol = ctx.SurfaceTol;
            var hardRemove = new List<SG_Element>();
            double totalLen = 0.0;
            double outsideLen = 0.0;
            double keptTotalLen = 0.0;
            double keptOutsideLen = 0.0;

            Stopwatch swSampling = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;
            foreach (var el in shape.Elems)
            {
                if (el is not SG_Elem1D e) continue;
                if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null) continue;
                Line ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);
                if (!ln.IsValid || ln.Length < 1e-9) continue;

                double r = OutsideRatio(ln, ctx, surfaceTol);
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
            Accum(swSampling, "Boundary[SampleElements]");

            if (mode == 1 && hardRemove.Count > 0)
            {
                Stopwatch swRemove = ProfileMsAccumulator != null ? Stopwatch.StartNew() : null;
                foreach (var el in hardRemove)
                {
                    if (el is SG_Elem1D e2 && e2.Nodes != null)
                        foreach (var n in e2.Nodes)
                            n?.Elements?.Remove(el);
                    shape.Elems.Remove(el);
                }
                shape.RemoveUnusedNodes();
                Accum(swRemove, "Boundary[RemoveElements]");
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

        private static void Accum(Stopwatch sw, string key)
        {
            if (sw == null) return;
            sw.Stop();
            var dict = ProfileMsAccumulator;
            if (dict == null) return;
            double ms = sw.Elapsed.TotalMilliseconds;
            if (dict.TryGetValue(key, out double existing))
                dict[key] = existing + ms;
            else
                dict[key] = ms;
        }
    }
}
