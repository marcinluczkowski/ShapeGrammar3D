using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Preview a single SH_SimpleShape (SG_Shape): elements (line + section mesh),
    /// nodes, supports, and loads as node+vector.
    /// </summary>
    public class SimpleShapePreview : GH_Component
    {
        public SimpleShapePreview()
          : base("SH_SimpleShape Preview", "SH_Preview",
              "Geometry preview for one assembled shape: element axis lines, extruded section meshes, node points, support boxes, and load vectors.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_SimpleShape", "SH",
                "Assembled shape (SG_Shape) from the Assembly component.", GH_ParamAccess.item);                // 0
            pManager.AddNumberParameter("Load Scale", "LdS",
                "Multiplier for drawing load force vectors (model units per kN). Increase if arrows are too short.", GH_ParamAccess.item, 0.02);          // 1
            pManager.AddNumberParameter("Support Size", "SupS",
                "Half-size of each support marker box (model units).", GH_ParamAccess.item, 0.25);                      // 2
            pManager.AddBooleanParameter("Show Section Mesh", "Mesh",
                "If true, extrude a rectangular mesh along each element using its cross-section width/height (mm→m).", GH_ParamAccess.item, true);             // 3

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Element Lines", "ElemLn", "Axis lines for each 1D element (endpoints).", GH_ParamAccess.list);        // 0
            pManager.AddMeshParameter("Element Meshes", "ElemM", "Solid meshes: rectangular profile swept along each element axis.", GH_ParamAccess.list); // 1
            pManager.AddPointParameter("Nodes", "Nodes", "All structural nodes (end nodes plus mid-nodes from loads).", GH_ParamAccess.list);                  // 2
            pManager.AddBrepParameter("Supports", "Sup", "Box breps marking restrained supports (non-free DOFs).", GH_ParamAccess.list);         // 3
            pManager.AddPointParameter("Load Points", "LdPt", "Anchor point for each displayed load (point load origin or line-load sample).", GH_ParamAccess.list);      // 4
            pManager.AddLineParameter("Load Vectors", "LdVec", "Line segments representing force vectors in space (scaled).", GH_ParamAccess.list);  // 5
            pManager.AddTextParameter("Info", "Info", "Counts and quick validation summary.", GH_ParamAccess.item);                          // 6
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            SG_Shape shape = null;
            double loadScale = 0.02;
            double supportSize = 0.25;
            bool showMesh = true;

            if (!DA.GetData(0, ref shape) || shape == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "SH_SimpleShape is required.");
                return;
            }

            DA.GetData(1, ref loadScale);
            DA.GetData(2, ref supportSize);
            DA.GetData(3, ref showMesh);

            loadScale = Math.Max(0.0, loadScale);
            supportSize = Math.Max(0.001, supportSize);

            var elementLines = new List<Line>();
            var elementMeshes = new List<Mesh>();
            var nodes = new List<Point3d>();
            var supports = new List<Brep>();
            var loadPoints = new List<Point3d>();
            var loadVectors = new List<Line>();

            // Elements and meshes
            if (shape.Elems != null)
            {
                foreach (var elem in shape.Elems.OfType<SG_Elem1D>())
                {
                    if (elem == null) continue;

                    Line ln = elem.Ln;
                    if (elem.Nodes != null && elem.Nodes.Length >= 2 && elem.Nodes[0] != null && elem.Nodes[1] != null)
                        ln = new Line(elem.Nodes[0].Pt, elem.Nodes[1].Pt);
                    elementLines.Add(ln);

                    if (!showMesh) continue;

                    double secW = 0.0;
                    double secH = 0.0;

                    if (elem.CrossSection is SH_CrossSection_RHS rhs)
                    {
                        secW = rhs.Width;
                        secH = rhs.Height;
                    }
                    else if (elem.CrossSection is SH_CrossSection_Rectangle rect)
                    {
                        secW = rect.width;
                        secH = rect.height;
                    }
                    else if (elem.CrossSection != null && elem.CrossSection.Area > 0)
                    {
                        // fallback: equivalent square by area (in mm)
                        secW = Math.Sqrt(elem.CrossSection.Area);
                        secH = secW;
                    }

                    Mesh m = ExtrudeRectSection(ln, secW * 0.001, secH * 0.001);
                    if (m != null) elementMeshes.Add(m);
                }
            }

            // Nodes (+ mid nodes for load-start visibility)
            if (shape.Nodes != null)
            {
                foreach (var nd in shape.Nodes)
                    if (nd != null) nodes.Add(nd.Pt);
            }

            if (shape.Elems != null)
            {
                foreach (var e in shape.Elems.OfType<SG_Elem1D>())
                {
                    if (e?.MidNodes == null) continue;
                    foreach (var mn in e.MidNodes)
                    {
                        if (mn == null) continue;
                        if (!nodes.Any(p => p.DistanceToSquared(mn.Pt) < UT.PRES * UT.PRES))
                            nodes.Add(mn.Pt);
                    }
                }
            }

            // Supports as simple boxes
            if (shape.Supports != null)
            {
                double h = supportSize * 0.5;
                foreach (var sup in shape.Supports)
                {
                    if (sup == null || sup.SupportCondition == 0) continue;
                    Point3d c = sup.Pt;
                    var box = new Box(
                        new Plane(c, Vector3d.ZAxis),
                        new Interval(-h, h),
                        new Interval(-h, h),
                        new Interval(-h, h));
                    Brep b = box.ToBrep();
                    if (b != null) supports.Add(b);
                }
            }

            // Point loads
            if (shape.PointLoads != null)
            {
                foreach (var pl in shape.PointLoads)
                {
                    if (pl == null) continue;
                    Point3d p = pl.Position;
                    Vector3d v = pl.Forces * loadScale;
                    loadPoints.Add(p);
                    loadVectors.Add(new Line(p, p + v));
                }
            }

            // Line loads (preview as vectors at element midpoint)
            if (shape.LineLoads != null && shape.Elems != null)
            {
                foreach (var ll in shape.LineLoads)
                {
                    if (ll == null) continue;

                    var targets = ResolveLineLoadTargets(shape, ll);
                    foreach (var e in targets)
                    {
                        if (e == null) continue;
                        Line ln = e.Ln;
                        if (e.Nodes != null && e.Nodes.Length >= 2 && e.Nodes[0] != null && e.Nodes[1] != null)
                            ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);

                        Point3d mid = ln.PointAt(0.5);
                        Vector3d v = ll.Load * loadScale;
                        loadPoints.Add(mid);
                        loadVectors.Add(new Line(mid, mid + v));
                    }
                }
            }

            DA.SetDataList(0, elementLines);
            DA.SetDataList(1, elementMeshes);
            DA.SetDataList(2, nodes);
            DA.SetDataList(3, supports);
            DA.SetDataList(4, loadPoints);
            DA.SetDataList(5, loadVectors);
            DA.SetData(6, string.Format(
                "SH_Preview: {0} elems, {1} meshes, {2} nodes, {3} supports, {4} load vectors",
                elementLines.Count, elementMeshes.Count, nodes.Count, supports.Count, loadVectors.Count));
        }

        private static List<SG_Elem1D> ResolveLineLoadTargets(SG_Shape shape, SG_LineLoad ll)
        {
            var elems = shape.Elems?.OfType<SG_Elem1D>().ToList() ?? new List<SG_Elem1D>();
            if (string.IsNullOrWhiteSpace(ll.ElementId)) return elems;

            var byName = elems.Where(e => string.Equals(e.Name, ll.ElementId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count > 0) return byName;

            if (int.TryParse(ll.ElementId, out int id))
                return elems.Where(e => e.ID == id).ToList();

            return new List<SG_Elem1D>();
        }

        /// <summary>
        /// Same rectangular section extrusion strategy as existing preview components.
        /// Width/height are in meters.
        /// </summary>
        private static Mesh ExtrudeRectSection(Line axis, double widthM, double heightM)
        {
            if (widthM <= 0 || heightM <= 0 || axis.Length < 1e-12)
                return null;

            double hw = widthM * 0.5;
            double hh = heightM * 0.5;

            Vector3d tangent = axis.UnitTangent;

            Vector3d localY;
            if (Math.Abs(tangent * Vector3d.ZAxis) > 0.99)
                localY = Vector3d.YAxis;
            else
                localY = Vector3d.CrossProduct(Vector3d.ZAxis, tangent);
            localY.Unitize();

            Vector3d localZ = Vector3d.CrossProduct(tangent, localY);
            localZ.Unitize();

            Point3d[] corners = new Point3d[8];
            for (int end = 0; end < 2; end++)
            {
                Point3d origin = end == 0 ? axis.From : axis.To;
                corners[end * 4 + 0] = origin - localY * hw - localZ * hh;
                corners[end * 4 + 1] = origin + localY * hw - localZ * hh;
                corners[end * 4 + 2] = origin + localY * hw + localZ * hh;
                corners[end * 4 + 3] = origin - localY * hw + localZ * hh;
            }

            Mesh mesh = new Mesh();
            mesh.Vertices.AddVertices(corners);

            mesh.Faces.AddFace(0, 1, 5, 4);
            mesh.Faces.AddFace(1, 2, 6, 5);
            mesh.Faces.AddFace(2, 3, 7, 6);
            mesh.Faces.AddFace(3, 0, 4, 7);
            mesh.Faces.AddFace(0, 3, 2, 1);
            mesh.Faces.AddFace(4, 5, 6, 7);

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("B9A55A47-98BD-4D83-8AE8-3E21E47C5BE1");
    }
}
