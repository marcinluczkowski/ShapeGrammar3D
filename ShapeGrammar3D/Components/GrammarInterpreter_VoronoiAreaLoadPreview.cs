using Grasshopper.Kernel;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Visualizes Voronoi tributary areas used for area-load distribution.
    /// </summary>
    public class GrammarInterpreter_VoronoiAreaLoadPreview : GH_Component
    {
        public GrammarInterpreter_VoronoiAreaLoadPreview()
          : base("GI_Voronoi AreaLoad Preview", "GI_VoroLoad",
              "Visualizes Voronoi partition (grid approximation) used for area-load distribution to AR2 strut endpoints nearer the roof boundary.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen", "Generation index. -1 = last generation.", GH_ParamAccess.item, -1);
            pManager.AddIntegerParameter("Individual", "Ind", "Individual index. -1 = best by fitness in selected generation.", GH_ParamAccess.item, -1);
            pManager.AddIntegerParameter("Grid Resolution", "Grid", "Grid resolution for Voronoi approximation (20-300).", GH_ParamAccess.item, 80);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Voronoi Mesh", "VoroM", "Colored Voronoi partition mesh on top projection rectangle.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Voronoi Boundaries", "VoroB", "Voronoi boundary polylines (grid-approximated).", GH_ParamAccess.list);
            pManager.AddPointParameter("Load Points", "Tips", "AR2 strut endpoints used as Voronoi seeds (roof-nearer of Nodes[0]/[1]).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tributary Areas", "Area", "Voronoi tributary area per load point.", GH_ParamAccess.list);
            pManager.AddVectorParameter("Point Loads", "Load", "Point load vectors sampled at each load point.", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_SGAssembly ghAssembly = null;
            int genReq = -1, indReq = -1, gridRes = 80;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly input is required.");
                return;
            }
            DA.GetData(1, ref genReq);
            DA.GetData(2, ref indReq);
            DA.GetData(3, ref gridRes);
            gridRes = Math.Clamp(gridRes, 20, 300);

            if (!TryGetTargetShape(ghAssembly.Value, genReq, indReq, out var shape, out string selectInfo))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not select a valid shape from assembly.");
                return;
            }

            BoundingBox bb = shape.BoundaryBrep != null ? shape.BoundaryBrep.GetBoundingBox(true)
                : (shape.BoundaryMesh != null ? shape.BoundaryMesh.GetBoundingBox(true) : shape.GetLinesFromShape().Aggregate(BoundingBox.Empty, (acc, ln) => { acc.Union(ln.BoundingBox); return acc; }));

            var tipNodes = VoronoiAreaLoadUtil.CollectStrutRoofNearerNodes(shape, bb);
            if (tipNodes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No AR2 strut load points found in selected shape.");
                return;
            }

            double xMin = bb.Min.X, xMax = bb.Max.X;
            double yMin = bb.Min.Y, yMax = bb.Max.Y;
            double zTop = bb.Max.Z;
            if (xMax - xMin < 1e-9 || yMax - yMin < 1e-9)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Boundary bbox is degenerate for Voronoi preview.");
                return;
            }

            var tips2D = tipNodes.Select(n => (node: n, x: n.Pt.X, y: n.Pt.Y)).ToList();
            var areas = ComputeVoronoiAreas(tips2D, xMin, xMax, yMin, yMax, gridRes, out int[,] ownerGrid);
            var mesh = BuildVoronoiMesh(tips2D, ownerGrid, xMin, xMax, yMin, yMax, zTop, gridRes);
            var boundaries = BuildVoronoiBoundaryPolylines(ownerGrid, xMin, xMax, yMin, yMax, zTop, gridRes);

            var loads = new List<Vector3d>();
            foreach (var nd in tipNodes)
            {
                Vector3d f = Vector3d.Zero;
                if (shape.PointLoads != null)
                {
                    foreach (var pl in shape.PointLoads)
                        if (pl != null && pl.Position.DistanceToSquared(nd.Pt) < 1e-6)
                            f += pl.Forces;
                }
                loads.Add(f);
            }

            DA.SetData(0, mesh);
            DA.SetDataList(1, boundaries);
            DA.SetDataList(2, tipNodes.Select(n => n.Pt).ToList());
            DA.SetDataList(3, tipNodes.Select(n => areas.GetValueOrDefault(n.ID, 0.0)).ToList());
            DA.SetDataList(4, loads);
            DA.SetData(5, $"{selectInfo}, LoadPts={tipNodes.Count}, Grid={gridRes}x{gridRes}, Boundaries={boundaries.Count}");
        }

        private static bool TryGetTargetShape(SGShapeGrammar3DAssembly assembly, int genReq, int indReq, out SG_Shape shape, out string info)
        {
            shape = null;
            info = "N/A";
            var gens = assembly?.Generations ?? new List<AssemblyGeneration>();
            if (gens.Count == 0) return false;

            AssemblyGeneration targetGen = genReq < 0
                ? gens.OrderBy(g => g.Generation).Last()
                : gens.FirstOrDefault(g => g.Generation == genReq);
            if (targetGen?.Individuals == null || targetGen.Individuals.Count == 0) return false;

            AssemblyIndividual ind = null;
            int idx = -1;
            if (indReq >= 0 && indReq < targetGen.Individuals.Count)
            {
                ind = targetGen.Individuals[indReq];
                idx = indReq;
            }
            else
            {
                var candidates = targetGen.Individuals
                    .Select((x, i) => new { x, i })
                    .Where(x => x.x?.Shape != null)
                    .OrderBy(x => x.x.Rank)
                    .ThenBy(x => x.x.Fitness)
                    .ToList();
                if (candidates.Count == 0) return false;
                ind = candidates[0].x;
                idx = candidates[0].i;
            }

            if (ind?.Shape == null) return false;
            shape = ind.Shape.DeepCopy();
            info = $"Gen={targetGen.Generation}, Ind={idx}";
            return true;
        }

        private static Dictionary<int, double> ComputeVoronoiAreas(
            List<(SG_Node node, double x, double y)> tips,
            double xMin, double xMax, double yMin, double yMax, int gridRes,
            out int[,] ownerGrid)
        {
            ownerGrid = new int[gridRes, gridRes];
            double totalW = xMax - xMin;
            double totalH = yMax - yMin;
            double totalArea = totalW * totalH;
            double cellArea = totalArea / (gridRes * gridRes);
            double dx = totalW / gridRes;
            double dy = totalH / gridRes;

            var counts = tips.ToDictionary(t => t.node.ID, _ => 0);
            for (int iy = 0; iy < gridRes; iy++)
            {
                double gy = yMin + (iy + 0.5) * dy;
                for (int ix = 0; ix < gridRes; ix++)
                {
                    double gx = xMin + (ix + 0.5) * dx;
                    double best = double.MaxValue;
                    int owner = tips[0].node.ID;
                    for (int i = 0; i < tips.Count; i++)
                    {
                        double ddx = gx - tips[i].x;
                        double ddy = gy - tips[i].y;
                        double d2 = ddx * ddx + ddy * ddy;
                        if (d2 < best)
                        {
                            best = d2;
                            owner = tips[i].node.ID;
                        }
                    }
                    ownerGrid[ix, iy] = owner;
                    counts[owner]++;
                }
            }
            return counts.ToDictionary(kv => kv.Key, kv => kv.Value * cellArea);
        }

        private static Mesh BuildVoronoiMesh(
            List<(SG_Node node, double x, double y)> tips,
            int[,] ownerGrid,
            double xMin, double xMax, double yMin, double yMax, double z, int gridRes)
        {
            var mesh = new Mesh();
            double dx = (xMax - xMin) / gridRes;
            double dy = (yMax - yMin) / gridRes;

            var colorMap = new Dictionary<int, Color>();
            for (int i = 0; i < tips.Count; i++)
                colorMap[tips[i].node.ID] = IndexedColor(i);

            for (int iy = 0; iy < gridRes; iy++)
            {
                double y0 = yMin + iy * dy;
                double y1 = y0 + dy;
                for (int ix = 0; ix < gridRes; ix++)
                {
                    double x0 = xMin + ix * dx;
                    double x1 = x0 + dx;

                    int v0 = mesh.Vertices.Add(x0, y0, z);
                    int v1 = mesh.Vertices.Add(x1, y0, z);
                    int v2 = mesh.Vertices.Add(x1, y1, z);
                    int v3 = mesh.Vertices.Add(x0, y1, z);
                    mesh.Faces.AddFace(v0, v1, v2, v3);

                    int owner = ownerGrid[ix, iy];
                    Color c = colorMap.TryGetValue(owner, out var cc) ? cc : Color.Gray;
                    mesh.VertexColors.Add(c);
                    mesh.VertexColors.Add(c);
                    mesh.VertexColors.Add(c);
                    mesh.VertexColors.Add(c);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private static Color IndexedColor(int i)
        {
            double t = (i * 0.61803398875) % 1.0;
            int r = (int)(127 + 128 * Math.Sin(2 * Math.PI * (t + 0.00)));
            int g = (int)(127 + 128 * Math.Sin(2 * Math.PI * (t + 0.33)));
            int b = (int)(127 + 128 * Math.Sin(2 * Math.PI * (t + 0.66)));
            return Color.FromArgb(90, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        private static List<PolylineCurve> BuildVoronoiBoundaryPolylines(
            int[,] ownerGrid, double xMin, double xMax, double yMin, double yMax, double z, int gridRes)
        {
            var curves = new List<PolylineCurve>();
            double dx = (xMax - xMin) / gridRes;
            double dy = (yMax - yMin) / gridRes;

            for (int iy = 0; iy < gridRes; iy++)
            {
                for (int ix = 0; ix < gridRes; ix++)
                {
                    int owner = ownerGrid[ix, iy];

                    if (ix < gridRes - 1 && owner != ownerGrid[ix + 1, iy])
                    {
                        double x = xMin + (ix + 1) * dx;
                        double y0 = yMin + iy * dy;
                        double y1 = y0 + dy;
                        var pl = new Polyline
                        {
                            new Point3d(x, y0, z),
                            new Point3d(x, y1, z)
                        };
                        curves.Add(new PolylineCurve(pl));
                    }

                    if (iy < gridRes - 1 && owner != ownerGrid[ix, iy + 1])
                    {
                        double y = yMin + (iy + 1) * dy;
                        double x0 = xMin + ix * dx;
                        double x1 = x0 + dx;
                        var pl = new Polyline
                        {
                            new Point3d(x0, y, z),
                            new Point3d(x1, y, z)
                        };
                        curves.Add(new PolylineCurve(pl));
                    }
                }
            }

            return curves;
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;
        public override Guid ComponentGuid => new Guid("F2EFD508-1B18-4EAF-9F89-4D6340AAB22F");
    }
}
