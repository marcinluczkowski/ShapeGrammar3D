using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
using System.Linq;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Components
{
    // This assembly component is not yet compatibel with other geometries than lines as for the simple bridge and truss roof grammar.
    [Serializable]
    public class Assembly : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Assembly class.
        /// </summary>
        public Assembly()
          : base("Assembly SH_Shape", "AsmSHShape",
              "Builds one SH_SimpleShape (SG_Shape) for the grammar interpreters: merges 1D elements, supports, and loads; snaps interior point loads onto beams as mid-nodes where needed; resolves curve loads.",
              UT.CAT, UT.GR_ASSEM)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Elements", "elems",
                "1D structural elements (SG_Elem1D from LineToElement / CurveToElement).", GH_ParamAccess.list); // 0
            pManager.AddGenericParameter("Supports", "sup",
                "Support objects (SG_Support). Placed at nodes that match support points.", GH_ParamAccess.list); // 1
            pManager.AddGenericParameter("Loads", "loads",
                "Loads: SG_PointLoad, SG_LineLoad, and/or SG_CurveLoad. Interior point loads are projected onto beams without splitting the SG element.", GH_ParamAccess.list); // 2

            // make the support and load parameters optional
            pManager[1].Optional = true;
            pManager[2].Optional = true;

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_SimpleShape", "SH",
                "Single SG_Shape: nodes, elements, supports, point loads, and line loads ready for rules / interpreters.", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            List<SG_Element> elems = new List<SG_Element>();
            List<SG_Support> sups = new List<SG_Support>();
            List<SG_Load> loads = new List<SG_Load>();
            

            // --- input ---
            if (!DA.GetDataList(0, elems)) return;
           
            if(!DA.GetDataList(1, sups))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "There are no supports in the assembly!");
            }

            if(!DA.GetDataList(2, loads))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "There are no loads in the assembly!");
            }

            // deep copy the input
            elems = UT.DeepCopyList(elems).Cast<SG_Element>().ToList();
            sups  = UT.DeepCopyList(sups);
            loads = UT.DeepCopyList(loads);


            // --- solve ---

            SG_Shape shape = new SG_Shape
            {
                elementCount = 0,
                nodeCount = 0
            };

            List<SG_Node> nodes = new List<SG_Node>();
            List<SG_Element> renumberedElems = new List<SG_Element>();
            List<SG_Support> uniqueSupports = new List<SG_Support>();

            foreach (SG_Element e in elems)
            {
                // element counter
                e.ID = shape.elementCount;
                shape.elementCount++;
                renumberedElems.Add(e);

                // node check and renumbering
                foreach (SG_Node nd in e.Nodes)
                {
                    SG_Node targetNode;

                    // test if there is already a node in this position
                    if (nodes.Any(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001 ))
                    {
                        targetNode = nodes.Where(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001).First();

                        targetNode.Elements.Add(e);
                        continue;
                    }
                    
                    // in case it is a new node
                    nd.ID = shape.nodeCount;
                    nd.Elements.Add(e);
                    nodes.Add(nd);
                    shape.nodeCount++;
                }
            }



            foreach (var sup in sups)
            {
                // find the index of the node where the support applies
                SG_Node nd = nodes.FirstOrDefault( n => n.Pt.DistanceToSquared(sup.Pt) < 0.001 );

                if (nd != null)
                {
                    sup.Node = nd;
                    nd.Support = sup;
                    uniqueSupports.Add(sup); 
                }
            }
            
            // join Init_Crv where possible
            JoinInitialCurves(renumberedElems);

            // partition the loads
            SortLoads(loads,
                out List<SG_LineLoad> l_loads,
                out List<SG_PointLoad> p_loads,
                out List<SG_CurveLoad> c_loads);

            // attach the shape state up-front so node IDs assigned during
            // load-resolution (mid-nodes) keep flowing through `shape.nodeCount`.
            shape.Nodes = nodes;
            shape.Elems = renumberedElems;

            // resolve curve loads against the assembled elements:
            // -> LineToBeams gets emitted as SG_LineLoad per overlapping element
            // -> PointsOnNodes is sampled along the curve, snapped to host beams
            //    as new mid-element nodes, and emitted as SG_PointLoad
            ResolveCurveLoads(shape, c_loads, l_loads, p_loads);

            // snap any user point loads whose position does NOT coincide with an
            // existing node onto the closest beam as a mid-element node.
            SnapPointLoadsToElements(shape, p_loads);

            shape.LineLoads = l_loads;
            shape.PointLoads = p_loads;
            shape.Supports = uniqueSupports;
            shape.SimpleShapeState = State.alpha;

            // --- output ---
            DA.SetData(0, shape); 


        }
        /// <summary>
        /// Private methods for this components
        /// </summary>
        /// 


        private void SortLoads(
            List<SG_Load> loads,
            out List<SG_LineLoad> line_loads,
            out List<SG_PointLoad> point_loads,
            out List<SG_CurveLoad> curve_loads)
        {
            var pl = new List<SG_PointLoad>();
            var ll = new List<SG_LineLoad>();
            var cl = new List<SG_CurveLoad>();

            foreach (var l in loads)
            {
                switch (l)
                {
                    case SG_CurveLoad cload: cl.Add(cload); break;
                    case SG_LineLoad load:   ll.Add(load);  break;
                    case SG_PointLoad ptl:   pl.Add(ptl);   break;
                }
            }

            line_loads = ll;
            point_loads = pl;
            curve_loads = cl;
        }

        /// <summary>
        /// Resolve every <see cref="SG_CurveLoad"/> against the assembled elements:
        ///   - PointsOnNodes -> sample the curve, snap each station to the closest beam
        ///     as a new mid-element node and emit an SG_PointLoad with the tributary
        ///     force at that node;
        ///   - LineToBeams -> for every element the curve overlaps, emit an SG_LineLoad
        ///     keyed on the element's name (or ID) so the FEM step can pick it up.
        /// </summary>
        private void ResolveCurveLoads(
            SG_Shape shape,
            List<SG_CurveLoad> curveLoads,
            List<SG_LineLoad> outLineLoads,
            List<SG_PointLoad> outPointLoads)
        {
            if (curveLoads == null || curveLoads.Count == 0) return;
            if (shape?.Elems == null || shape.Elems.Count == 0) return;

            var beams = new List<SG_Elem1D>();
            foreach (var e in shape.Elems) if (e is SG_Elem1D e1d) beams.Add(e1d);
            if (beams.Count == 0) return;

            foreach (var cl in curveLoads)
            {
                if (cl?.Curve == null) continue;

                if (cl.Distribution == CurveLoadDistribution.LineToBeams)
                {
                    EmitLineToBeams(beams, cl, outLineLoads);
                }
                else
                {
                    EmitPointsOnNodes(shape, beams, cl, outPointLoads);
                }
            }
        }

        private static void EmitLineToBeams(List<SG_Elem1D> beams, SG_CurveLoad cl, List<SG_LineLoad> outLineLoads)
        {
            double tol = UT.PRES;
            double total = cl.Curve.GetLength();
            if (total <= 0 || cl.Force.Length < 1e-12) return;

            int samples = Math.Max(20, cl.Subdivisions * 4);
            var counts = new Dictionary<int, int>();
            int hitTotal = 0;

            for (int i = 0; i <= samples; i++)
            {
                double t = (double)i / samples;
                Point3d pt = cl.Curve.PointAtNormalizedLength(t);
                int idx = FindClosestBeam(beams, pt, tol * 50);
                if (idx < 0) continue;
                counts.TryGetValue(idx, out int c);
                counts[idx] = c + 1;
                hitTotal++;
            }

            if (hitTotal == 0) return;

            foreach (var kv in counts)
            {
                outLineLoads.Add(new SG_LineLoad(cl.LoadCase, cl.Force)
                {
                    ElementId = beams[kv.Key].Name ?? beams[kv.Key].ID.ToString()
                });
            }
        }

        private static void EmitPointsOnNodes(
            SG_Shape shape,
            List<SG_Elem1D> beams,
            SG_CurveLoad cl,
            List<SG_PointLoad> outPointLoads)
        {
            int n = Math.Max(2, cl.Subdivisions);
            double total = cl.Curve.GetLength();
            if (total <= 0) return;

            // Tributary length per station: closed/uniform sampling assumes equal
            // arc-length spacing, with end stations getting half the tributary.
            double dl = total / (n - 1);

            for (int i = 0; i < n; i++)
            {
                double s = (n == 1) ? 0.5 : (double)i / (n - 1);
                Point3d pt = cl.Curve.PointAtNormalizedLength(s);

                int beamIdx = FindClosestBeam(beams, pt, double.MaxValue);
                if (beamIdx < 0) continue;

                SG_Elem1D host = beams[beamIdx];
                Point3d snapped = ProjectToBeam(host, pt, out double t);

                bool isEnd = (i == 0) || (i == n - 1);
                double tribLen = isEnd ? dl * 0.5 : dl;
                Vector3d force = cl.Force * tribLen;

                // Either reuse an endpoint, an existing mid-node, or create a new
                // mid-node on the host beam. In all cases emit a point load.
                SG_Node target = ReuseOrCreateMidNode(shape, host, snapped, t);
                outPointLoads.Add(new SG_PointLoad(force, Vector3d.Zero, target.Pt));
            }
        }

        /// <summary>
        /// Snap any input point load whose position is not on an existing node
        /// onto the nearest beam, register a mid-element node, and rewrite
        /// the load's position to the snapped point.
        /// </summary>
        private void SnapPointLoadsToElements(SG_Shape shape, List<SG_PointLoad> pointLoads)
        {
            if (pointLoads == null || pointLoads.Count == 0) return;
            if (shape?.Elems == null || shape.Nodes == null) return;

            var beams = new List<SG_Elem1D>();
            foreach (var e in shape.Elems) if (e is SG_Elem1D e1d) beams.Add(e1d);
            if (beams.Count == 0) return;

            foreach (var pl in pointLoads)
            {
                Point3d p = pl.Position;
                bool onExistingNode = shape.Nodes.Any(n => n != null && n.Pt.DistanceToSquared(p) < UT.PRES * UT.PRES);
                if (onExistingNode) continue;

                int beamIdx = FindClosestBeam(beams, p, double.MaxValue);
                if (beamIdx < 0) continue;

                Point3d snapped = ProjectToBeam(beams[beamIdx], p, out double t);
                SG_Node target = ReuseOrCreateMidNode(shape, beams[beamIdx], snapped, t);
                pl.Position = target.Pt;
            }
        }

        // ---------------------------------------------------------------
        // Helpers shared between the two paths above.
        // ---------------------------------------------------------------

        private static int FindClosestBeam(List<SG_Elem1D> beams, Point3d pt, double maxDist)
        {
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < beams.Count; i++)
            {
                Point3d p = ProjectToBeam(beams[i], pt, out _);
                double d = p.DistanceTo(pt);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return bestDist <= maxDist ? best : -1;
        }

        private static Point3d ProjectToBeam(SG_Elem1D beam, Point3d pt, out double t01)
        {
            // Prefer the curve when it's actually curved; fall back to the chord.
            if (beam.Crv != null && beam.Crv.IsValid)
            {
                if (beam.Crv.ClosestPoint(pt, out double tc))
                {
                    var dom = beam.Crv.Domain;
                    t01 = (dom.T1 - dom.T0) > 1e-12
                        ? (tc - dom.T0) / (dom.T1 - dom.T0)
                        : 0.0;
                    return beam.Crv.PointAt(tc);
                }
            }

            // Chord fallback.
            Point3d a = beam.Nodes[0].Pt;
            Point3d b = beam.Nodes[1].Pt;
            Vector3d ab = b - a;
            double len2 = ab.SquareLength;
            if (len2 < 1e-18) { t01 = 0.0; return a; }
            t01 = Math.Clamp(((pt - a) * ab) / len2, 0.0, 1.0);
            return a + t01 * ab;
        }

        /// <summary>
        /// Returns the node at <paramref name="snapped"/> on <paramref name="host"/>:
        /// reuses an endpoint or an already-registered mid-node when the position
        /// matches; otherwise creates a brand-new mid-node, registers it on the
        /// shape (assigning an ID via <see cref="SG_Shape.nodeCount"/>) and on
        /// the host beam's <see cref="SG_Elem1D.MidNodes"/>.
        /// </summary>
        private static SG_Node ReuseOrCreateMidNode(SG_Shape shape, SG_Elem1D host, Point3d snapped, double t01)
        {
            double tol2 = UT.PRES * UT.PRES;

            // endpoint reuse
            if (host.Nodes != null && host.Nodes.Length >= 2)
            {
                if (host.Nodes[0]?.Pt.DistanceToSquared(snapped) < tol2) return host.Nodes[0];
                if (host.Nodes[1]?.Pt.DistanceToSquared(snapped) < tol2) return host.Nodes[1];
            }

            // existing mid-node reuse
            if (host.MidNodes != null)
            {
                foreach (var mn in host.MidNodes)
                {
                    if (mn != null && mn.Pt.DistanceToSquared(snapped) < tol2) return mn;
                }
            }

            // existing shape-wide node reuse (e.g. a node from a different beam)
            var existing = shape.Nodes?.FirstOrDefault(n => n != null && n.Pt.DistanceToSquared(snapped) < tol2);
            if (existing != null)
            {
                if (!existing.Elements.Contains(host)) existing.Elements.Add(host);
                if (host.MidNodes == null) host.MidNodes = new List<SG_Node>();
                if (!host.MidNodes.Contains(existing)) host.MidNodes.Add(existing);
                return existing;
            }

            // brand-new mid-node
            var nd = new SG_Node(snapped, shape.nodeCount);
            shape.nodeCount++;
            nd.Elements.Add(host);

            if (host.Crv != null)
            {
                var pl = new Plane();
                if (host.Crv.FrameAt(host.Crv.Domain.ParameterAt(t01), out pl)) nd.NPln = pl;
            }

            shape.Nodes.Add(nd);
            if (host.MidNodes == null) host.MidNodes = new List<SG_Node>();
            host.MidNodes.Add(nd);
            return nd;
        }

        private void JoinInitialCurves(List<SG_Element> elements)
        {
            var elem1DList = elements.OfType<SG_Elem1D>().Where(e => e.Init_Crv != null).ToList();

            if (elem1DList.Count == 0) return;

            var processed = new HashSet<int>();

            foreach (var elem in elem1DList)
            {
                if (processed.Contains(elem.ID)) continue;

                var curvesToJoin = new List<Curve> { elem.Init_Crv };
                var groupedElements = new List<SG_Elem1D> { elem };
                processed.Add(elem.ID);

                bool foundConnection = true;
                while (foundConnection)
                {
                    foundConnection = false;

                    foreach (var otherElem in elem1DList)
                    {
                        if (processed.Contains(otherElem.ID)) continue;

                        bool canJoin = false;
                        foreach (var existingCrv in curvesToJoin.ToList())
                        {
                            if (AreCurvesConnectable(existingCrv, otherElem.Init_Crv))
                            {
                                canJoin = true;
                                break;
                            }
                        }

                        if (canJoin)
                        {
                            curvesToJoin.Add(otherElem.Init_Crv);
                            groupedElements.Add(otherElem);
                            processed.Add(otherElem.ID);
                            foundConnection = true;
                        }
                    }
                }

                if (curvesToJoin.Count > 1)
                {
                    var joinedCurves = Curve.JoinCurves(curvesToJoin, UT.PRES);

                    if (joinedCurves != null && joinedCurves.Length > 0)
                    {
                        var joinedNurbsCurve = joinedCurves[0].ToNurbsCurve();
                        foreach (var groupedElem in groupedElements)
                        {
                            groupedElem.Joined_Init_Crv = joinedNurbsCurve;
                        }
                    }
                }
                else
                {
                    elem.Joined_Init_Crv = elem.Init_Crv?.ToNurbsCurve();
                }
            }
        }

        private bool AreCurvesConnectable(Curve crv1, Curve crv2)
        {
            if (crv1 == null || crv2 == null) return false;

            double tolerance = UT.PRES;

            return crv1.PointAtStart.DistanceTo(crv2.PointAtStart) < tolerance ||
                   crv1.PointAtStart.DistanceTo(crv2.PointAtEnd) < tolerance ||
                   crv1.PointAtEnd.DistanceTo(crv2.PointAtStart) < tolerance ||
                   crv1.PointAtEnd.DistanceTo(crv2.PointAtEnd) < tolerance;
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.icons_C_Mdl;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("d3d9eb87-86c0-4891-9d50-6810495145af"); }
        }
    }
}