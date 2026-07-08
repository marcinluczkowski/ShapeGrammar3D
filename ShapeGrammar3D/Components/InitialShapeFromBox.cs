using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Components
{
    [Serializable]
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class InitialShapeFromBox : GH_Component
    {
        public InitialShapeFromBox()
          : base("InitialShapeFromBox", "BoxShape",
              "Generate an initial SG_Shape from a bounding box and support positions",
              UT.CAT, UT.GR_ASSEM)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Support Points", "supPts", "Support positions (beam endpoints)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Cross Section", "crossSec", "Cross section for all elements", GH_ParamAccess.item);
            pManager.AddVectorParameter("Load", "load", "Load vector applied to every node", GH_ParamAccess.item, new Vector3d(0, 0, -100));
            pManager.AddTextParameter("Support Condition", "supCond", "[Tx,Ty,Tz,Rx,Ry,Rz] 1=locked 0=free", GH_ParamAccess.item, "111111");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_SimpleShape", "shape", "Assembled initial shape", GH_ParamAccess.item);
            pManager.AddLineParameter("Lines", "lines", "Generated line geometry (for preview)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var supPts = new List<Point3d>();
            SH_CrossSection_Beam crossSec = null;
            var loadVec = new Vector3d();
            string supCond = "111111";

            if (!DA.GetDataList(0, supPts)) return;
            if (!DA.GetData(1, ref crossSec)) return;
            DA.GetData(2, ref loadVec);
            DA.GetData(3, ref supCond);

            if (supPts.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least 2 support points are required.");
                return;
            }

            var elements = new List<SG_Element>();
            var supports = new List<SG_Support>();
            var loads = new List<SG_PointLoad>();
            var previewLines = new List<Line>();

            var beamLines = ConnectNodes(supPts);
            foreach (Line bl in beamLines)
            {
                var beam = new SG_Elem1D(bl, -999, "beam", crossSec);
                elements.Add(beam);
                previewLines.Add(bl);
            }

            foreach (Point3d sp in supPts)
            {
                supports.Add(new SG_Support(supCond, sp));
                loads.Add(new SG_PointLoad(loadVec, Vector3d.Zero, sp));
            }

            SG_Shape shape = AssembleShape(elements, supports, loads);

            DA.SetData(0, shape);
            DA.SetDataList(1, previewLines);
        }

        private List<Line> ConnectNodes(List<Point3d> pts)
        {
            var lines = new List<Line>();
            if (pts.Count < 2) return lines;

            if (pts.Count == 2)
            {
                lines.Add(new Line(pts[0], pts[1]));
                return lines;
            }

            var pts2d = pts.Select(p => new Point3d(p.X, p.Y, 0)).ToList();
            var sorted = SortByConvexHull2D(pts2d, pts);

            for (int i = 0; i < sorted.Count; i++)
            {
                int next = (i + 1) % sorted.Count;
                var line = new Line(sorted[i], sorted[next]);
                if (!lines.Any(l => IsDuplicateLine(l, line)))
                    lines.Add(line);
            }

            return lines;
        }

        private List<Point3d> SortByConvexHull2D(List<Point3d> flat, List<Point3d> original)
        {
            var centroid = new Point3d(
                flat.Average(p => p.X),
                flat.Average(p => p.Y),
                0);

            var indexed = original
                .Select((p, i) => new { Pt = p, Angle = Math.Atan2(flat[i].Y - centroid.Y, flat[i].X - centroid.X) })
                .OrderBy(x => x.Angle)
                .Select(x => x.Pt)
                .ToList();

            return indexed;
        }

        private bool IsDuplicateLine(Line a, Line b)
        {
            double tol = UT.PRES;
            return (a.From.DistanceToSquared(b.From) < tol && a.To.DistanceToSquared(b.To) < tol) ||
                   (a.From.DistanceToSquared(b.To) < tol && a.To.DistanceToSquared(b.From) < tol);
        }

        private SG_Shape AssembleShape(List<SG_Element> elems, List<SG_Support> sups, List<SG_PointLoad> pLoads)
        {
            var shape = new SG_Shape { elementCount = 0, nodeCount = 0 };
            var nodes = new List<SG_Node>();
            var renumbered = new List<SG_Element>();
            var uniqueSupports = new List<SG_Support>();

            foreach (SG_Element e in elems)
            {
                e.ID = shape.elementCount;
                shape.elementCount++;
                renumbered.Add(e);

                foreach (SG_Node nd in e.Nodes)
                {
                    if (nodes.Any(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001))
                    {
                        var existing = nodes.First(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001);
                        existing.Elements.Add(e);
                        continue;
                    }

                    nd.ID = shape.nodeCount;
                    nd.Elements.Add(e);
                    nodes.Add(nd);
                    shape.nodeCount++;
                }
            }

            foreach (var sup in sups)
            {
                SG_Node nd = nodes.FirstOrDefault(n => n.Pt.DistanceToSquared(sup.Pt) < 0.001);
                if (nd != null)
                {
                    sup.Node = nd;
                    nd.Support = sup;
                    uniqueSupports.Add(sup);
                }
            }

            JoinInitialCurves(renumbered);

            shape.Nodes = nodes;
            shape.Elems = renumbered;
            shape.Supports = uniqueSupports;
            shape.PointLoads = pLoads;
            shape.LineLoads = new List<SG_LineLoad>();
            shape.SimpleShapeState = State.alpha;

            return shape;
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
                var grouped = new List<SG_Elem1D> { elem };
                processed.Add(elem.ID);

                bool found = true;
                while (found)
                {
                    found = false;
                    foreach (var other in elem1DList)
                    {
                        if (processed.Contains(other.ID)) continue;
                        if (curvesToJoin.Any(c => AreCurvesConnectable(c, other.Init_Crv)))
                        {
                            curvesToJoin.Add(other.Init_Crv);
                            grouped.Add(other);
                            processed.Add(other.ID);
                            found = true;
                        }
                    }
                }

                if (curvesToJoin.Count > 1)
                {
                    var joined = Curve.JoinCurves(curvesToJoin, UT.PRES);
                    if (joined != null && joined.Length > 0)
                    {
                        var nc = joined[0].ToNurbsCurve();
                        foreach (var g in grouped) g.Joined_Init_Crv = nc;
                    }
                }
                else
                {
                    elem.Joined_Init_Crv = elem.Init_Crv?.ToNurbsCurve();
                }
            }
        }

        private bool AreCurvesConnectable(Curve a, Curve b)
        {
            if (a == null || b == null) return false;
            double tol = UT.PRES;
            return a.PointAtStart.DistanceTo(b.PointAtStart) < tol ||
                   a.PointAtStart.DistanceTo(b.PointAtEnd) < tol ||
                   a.PointAtEnd.DistanceTo(b.PointAtStart) < tol ||
                   a.PointAtEnd.DistanceTo(b.PointAtEnd) < tol;
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_CAT_Utilities;
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }
}
