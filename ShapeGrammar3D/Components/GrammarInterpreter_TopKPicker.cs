using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Top-K interactive picker. Reads an Assembly, picks K candidates from
    /// the last generation according to the selected mode, lays them out as
    /// scaled-down anchored previews stacked along +Z relative to the init curve,
    /// and lets
    /// the designer pick one (Pick Index) to output its lines + cross-section
    /// extrusion meshes for downstream Grasshopper use.
    ///
    /// Modes (Mode input):
    ///   0 = Top-K by Displacement (Fitness ascending)
    ///   1 = Top-K by Feasibility (ObjectiveValues[2] ascending)
    ///   2 = Top-1 per Cluster (up to K clusters, by Fitness)
    ///   3 = Top-K from Pareto front (Rank == 0, by Fitness)
    /// </summary>
    public class GrammarInterpreter_TopKPicker : GH_Component
    {
        private readonly List<(Point3d anchor, string text, Color color)> _viewportLabels =
            new List<(Point3d, string, Color)>();
        private BoundingBox _previewBounds = BoundingBox.Empty;

        public GrammarInterpreter_TopKPicker()
          : base("Grammar Interpreter Top-K Picker", "GI_TopK",
              "Picks top-K candidates from Assembly and previews them as scaled mini-structures stacked along world Z above the input init curve (Preview Spacing steps along Z). The Pick Index input outputs lines + section meshes for the chosen one.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from a GA run.", GH_ParamAccess.item); // 0
            pManager.AddCurveParameter("Init Curve", "Curve", "Init curve (same as the inicurve in the SG_Shape) used as positioning anchor for the previews.", GH_ParamAccess.item); // 1
            pManager.AddIntegerParameter("Mode", "Mode",
                "0=Top-K by Displacement, 1=Top-K by Feasibility, 2=Top-1 per Cluster, 3=Top-K from Pareto front.",
                GH_ParamAccess.item, 0);                                                                                              // 2
            pManager.AddIntegerParameter("K", "K", "Number of preview candidates.", GH_ParamAccess.item, 5);                          // 3
            pManager.AddIntegerParameter("Pick Index", "Pick", "Index (0..K-1) of the candidate to output as the picked structure.", GH_ParamAccess.item, 0); // 4
            pManager.AddNumberParameter("Preview Spacing", "dZ", "Spacing step along world Z between mini-preview cells [m].", GH_ParamAccess.item, 15.0); // 5
            pManager.AddNumberParameter("Preview Cell Size", "Cell", "Side length [m] of the bounding cell each mini-preview is scaled to fit. 0 = no scaling.", GH_ParamAccess.item, 10.0); // 6
            pManager.AddBooleanParameter("Show Meshes", "Mesh", "Generate cross-section extrusion meshes for previews.", GH_ParamAccess.item, false); // 7
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional: orient stacked previews (XY through layout origin) onto this plane.",
                GH_ParamAccess.item); // 8

            pManager[1].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Picker summary: K candidates with displacement, feasibility, cluster, rank.", GH_ParamAccess.item); // 0
            pManager.AddLineParameter("All Preview Lines", "Lines", "Lines of all K mini-previews. Tree path {previewIndex}.", GH_ParamAccess.tree); // 1
            pManager.AddMeshParameter("All Preview Meshes", "Meshes", "Cross-section extrusion meshes for the previews (only when Show Meshes = true). Tree path {previewIndex}.", GH_ParamAccess.tree); // 2
            pManager.AddTextParameter("Labels", "Labels", "Per-preview label strings. Tree path {previewIndex}.", GH_ParamAccess.tree);                  // 3
            pManager.AddPointParameter("Label Points", "LabelPts", "Anchor point for each label (offset upward in Z above each mini-preview cell).", GH_ParamAccess.tree); // 4
            pManager.AddLineParameter("Picked Lines", "PickL", "Element lines of the picked structure (in original world coordinates).", GH_ParamAccess.list); // 5
            pManager.AddMeshParameter("Picked Meshes", "PickM", "Cross-section extrusion meshes of the picked structure.", GH_ParamAccess.list);             // 6
            pManager.AddTextParameter("Picked Info", "PickInfo", "Stats of the picked individual.", GH_ParamAccess.item);                                    // 7
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _viewportLabels.Clear();
            _previewBounds = BoundingBox.Empty;

            GH_SGAssembly ghAssembly = null;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly required.");
                return;
            }
            var assembly = ghAssembly.Value;

            Curve initCurve = null;
            DA.GetData(1, ref initCurve);

            int mode = 0, k = 5, pickIndex = 0;
            double spacing = 15.0, cellSize = 10.0;
            bool showMeshes = false;
            DA.GetData(2, ref mode);
            DA.GetData(3, ref k);
            DA.GetData(4, ref pickIndex);
            DA.GetData(5, ref spacing);
            DA.GetData(6, ref cellSize);
            DA.GetData(7, ref showMeshes);

            k = Math.Max(1, k);
            mode = Math.Clamp(mode, 0, 3);

            // ── Resolve last generation candidates ─────────────────────
            if (assembly.Generations == null || assembly.Generations.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                return;
            }

            var lastGen = assembly.Generations.Last();
            var allInds = (lastGen.Individuals ?? new List<AssemblyIndividual>())
                .Where(i => i?.Shape != null
                         && double.IsFinite(i.Fitness)
                         && i.Fitness < double.MaxValue * 0.5)
                .ToList();

            if (allInds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Last generation has no valid individuals.");
                return;
            }

            // ── Pick top-K ─────────────────────────────────────────────
            var picks = SelectTopK(allInds, mode, k);
            if (picks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No candidates matched the selected mode.");
                return;
            }

            // ── Layout anchor (from input curve or origin) ─────────────
            Point3d origin;
            if (initCurve != null && initCurve.IsValid)
            {
                var bb = initCurve.GetBoundingBox(true);
                // First stacked cell sits above the curve in Z; XY aligned to curve centroid.
                origin = new Point3d(bb.Center.X, bb.Center.Y, bb.Max.Z + spacing);
            }
            else
            {
                origin = Point3d.Origin;
            }

            Transform dispXf = PreviewLayoutTransforms.GetOptionalDisplayTransform(DA, 8, origin);

            // ── Build previews ────────────────────────────────────────
            var linesTree = new GH_Structure<GH_Line>();
            var meshesTree = new GH_Structure<GH_Mesh>();
            var labelsTree = new GH_Structure<GH_String>();
            var labelPtsTree = new GH_Structure<GH_Point>();

            for (int i = 0; i < picks.Count; i++)
            {
                var ind = picks[i];
                var path = new GH_Path(i);

                var (linesScaled, meshesScaled, cellAnchor, cellTopCenter) =
                    BuildPreviewCell(ind, origin, i, spacing, cellSize, showMeshes);

                foreach (var ln in linesScaled)
                    linesTree.Append(new GH_Line(ln), path);

                if (showMeshes)
                {
                    foreach (var m in meshesScaled)
                        meshesTree.Append(new GH_Mesh(m), path);
                }

                string label = BuildLabel(ind, i, mode);
                labelsTree.Append(new GH_String(label), path);
                labelPtsTree.Append(new GH_Point(cellTopCenter), path);

                _viewportLabels.Add((cellTopCenter, label, GetLabelColor(i, picks.Count)));
                _previewBounds.Union(new BoundingBox(cellAnchor - new Vector3d(cellSize, cellSize, cellSize),
                                                     cellAnchor + new Vector3d(cellSize * 2, cellSize * 2, cellSize)));
            }

            // ── Picked individual: full-resolution lines + section meshes ──
            int safeIdx = Math.Clamp(pickIndex, 0, picks.Count - 1);
            var pickedInd = picks[safeIdx];
            var pickedLines = ExtractLines(pickedInd);
            var pickedMeshes = showMeshes ? ExtractMeshes(pickedInd) : new List<Mesh>();
            string pickedInfo = string.Format(
                "Picked {0}: id={1}, cluster={2}, rank={3}, dispRatio={4:F4}, util={5:F4}, feas={6:F4}",
                safeIdx, pickedInd.Id, pickedInd.ClustGrp, pickedInd.Rank,
                pickedInd.Fitness, pickedInd.ObjUtil, pickedInd.ObjFeas);

            // ── Info string ────────────────────────────────────────────
            var info = new System.Text.StringBuilder();
            info.AppendLine(string.Format("GI_TopK Picker: mode={0} K={1}/{2}", ModeName(mode), picks.Count, k));
            info.AppendLine("Idx | Id | Cluster | Rank | DispRatio | Util | Feas");
            info.AppendLine(new string('-', 70));
            for (int i = 0; i < picks.Count; i++)
            {
                var p = picks[i];
                info.AppendLine(string.Format(
                    "{0,3} | {1,5} | {2,7} | {3,4} | {4,9:F4} | {5,7:F4} | {6,7:F4}",
                    i, p.Id, p.ClustGrp, p.Rank, p.Fitness, p.ObjUtil, p.ObjFeas));
            }
            info.AppendLine();
            info.AppendLine("Picked: " + pickedInfo);

            if (!dispXf.IsIdentity)
            {
                linesTree = PreviewLayoutTransforms.TransformLineTree(linesTree, dispXf);
                meshesTree = PreviewLayoutTransforms.TransformMeshTree(meshesTree, dispXf);
                labelPtsTree = PreviewLayoutTransforms.TransformPointTree(labelPtsTree, dispXf);
                for (int li = 0; li < _viewportLabels.Count; li++)
                {
                    var vl = _viewportLabels[li];
                    Point3d a = vl.anchor;
                    a.Transform(dispXf);
                    _viewportLabels[li] = (a, vl.text, vl.color);
                }
                _previewBounds.Transform(dispXf);
            }

            DA.SetData(0, info.ToString());
            DA.SetDataTree(1, linesTree);
            DA.SetDataTree(2, meshesTree);
            DA.SetDataTree(3, labelsTree);
            DA.SetDataTree(4, labelPtsTree);
            DA.SetDataList(5, pickedLines);
            DA.SetDataList(6, pickedMeshes);
            DA.SetData(7, pickedInfo);
        }

        // ─── Selection ────────────────────────────────────────────────

        private List<AssemblyIndividual> SelectTopK(List<AssemblyIndividual> all, int mode, int k)
        {
            switch (mode)
            {
                case 0:
                    return all.OrderBy(i => i.Fitness).Take(k).ToList();
                case 1:
                    return all.OrderBy(i => i.ObjFeas).Take(k).ToList();
                case 2:
                {
                    var perCluster = all
                        .GroupBy(i => i.ClustGrp)
                        .OrderBy(g => g.Key)
                        .Select(g => g.OrderBy(i => i.Fitness).First())
                        .Take(k)
                        .ToList();
                    return perCluster;
                }
                case 3:
                {
                    var rankZero = all.Where(i => i.Rank == 0).OrderBy(i => i.Fitness).Take(k).ToList();
                    if (rankZero.Count == 0)
                        rankZero = all.OrderBy(i => i.Rank).ThenBy(i => i.Fitness).Take(k).ToList();
                    return rankZero;
                }
                default:
                    return all.OrderBy(i => i.Fitness).Take(k).ToList();
            }
        }

        private static string ModeName(int mode) => mode switch
        {
            0 => "Top by Displacement",
            1 => "Top by Feasibility",
            2 => "Top per Cluster",
            3 => "Pareto Front",
            _ => "?"
        };

        // ─── Preview cell construction ────────────────────────────────

        private (List<Line> lines, List<Mesh> meshes, Point3d cellAnchor, Point3d cellTopCenter)
            BuildPreviewCell(AssemblyIndividual ind, Point3d origin, int slot, double spacing, double cellSize, bool showMeshes)
        {
            var rawLines = ExtractLines(ind);
            var rawMeshes = showMeshes ? ExtractMeshes(ind) : new List<Mesh>();

            var bb = BoundingBox.Empty;
            foreach (var ln in rawLines)
            {
                bb.Union(ln.From);
                bb.Union(ln.To);
            }

            double scale = 1.0;
            Vector3d translation;
            Point3d cellAnchor = origin + new Vector3d(0, 0, slot * (cellSize + spacing));

            if (bb.IsValid && cellSize > 1e-9)
            {
                double diag = bb.Diagonal.Length;
                if (diag > 1e-9)
                {
                    scale = cellSize / diag;
                }
                translation = (Vector3d)(cellAnchor - bb.Center * scale);
            }
            else
            {
                translation = (Vector3d)(cellAnchor - Point3d.Origin);
            }

            var xform = Transform.Scale(Point3d.Origin, scale);
            xform = Transform.Translation(translation) * xform;

            var outLines = rawLines.Select(ln =>
            {
                Line copy = ln;
                copy.Transform(xform);
                return copy;
            }).ToList();

            var outMeshes = rawMeshes.Select(m =>
            {
                var copy = m.DuplicateMesh();
                copy.Transform(xform);
                return copy;
            }).ToList();

            Point3d cellTopCenter = cellAnchor + new Vector3d(0, 0, cellSize * 0.6);
            return (outLines, outMeshes, cellAnchor, cellTopCenter);
        }

        private string BuildLabel(AssemblyIndividual ind, int idx, int mode)
        {
            switch (mode)
            {
                case 0: return string.Format("[{0}] disp={1:F3}", idx, ind.Fitness);
                case 1: return string.Format("[{0}] feas={1:F3}", idx, ind.ObjFeas);
                case 2: return string.Format("[{0}] cl={1} disp={2:F3}", idx, ind.ClustGrp, ind.Fitness);
                case 3: return string.Format("[{0}] rank={1} disp={2:F3}", idx, ind.Rank, ind.Fitness);
                default: return string.Format("[{0}] disp={1:F3}", idx, ind.Fitness);
            }
        }

        private static Color GetLabelColor(int idx, int total)
        {
            if (total <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)idx / Math.Max(1, total - 1);
            int g = (int)((1 - t) * 220 + 35);
            int b = (int)(t * 220 + 35);
            return Color.FromArgb(40, g, b);
        }

        // ─── Geometry extraction ──────────────────────────────────────

        private static List<Line> ExtractLines(AssemblyIndividual ind)
        {
            var lines = new List<Line>();
            if (ind?.Shape?.Elems == null) return lines;
            foreach (var el in ind.Shape.Elems)
            {
                if (el is SG_Elem1D e1d && e1d.Ln.Length > 1e-9)
                    lines.Add(new Line(e1d.Ln.From, e1d.Ln.To));
            }
            return lines;
        }

        private static List<Mesh> ExtractMeshes(AssemblyIndividual ind)
        {
            var meshes = new List<Mesh>();
            if (ind?.Shape?.Elems == null) return meshes;

            foreach (var el in ind.Shape.Elems)
            {
                if (!(el is SG_Elem1D e1d) || e1d.Ln.Length < 1e-9) continue;

                double widthM = 0, heightM = 0;
                if (e1d.CrossSection is SH_CrossSection_Rectangle rect)
                {
                    widthM = rect.width * 0.001;
                    heightM = rect.height * 0.001;
                }
                else if (e1d.CrossSection is SH_CrossSection_RHS rhs)
                {
                    widthM = rhs.Width * 0.001;
                    heightM = rhs.Height * 0.001;
                }
                else if (e1d.CrossSection != null && e1d.CrossSection.Area > 0)
                {
                    double dim = Math.Sqrt(e1d.CrossSection.Area) * 0.001;
                    widthM = dim;
                    heightM = dim;
                }
                if (widthM <= 0 || heightM <= 0) continue;

                var mesh = ExtrudeRectSection(new Line(e1d.Ln.From, e1d.Ln.To), widthM, heightM);
                if (mesh != null) meshes.Add(mesh);
            }
            return meshes;
        }

        /// <summary>
        /// Builds a rectangular box mesh extruded along an axis. Adapted from
        /// GrammarInterpreter_ShapePreview.ExtrudeRectSection.
        /// </summary>
        private static Mesh ExtrudeRectSection(Line axis, double widthM, double heightM)
        {
            if (widthM <= 0 || heightM <= 0 || axis.Length < 1e-12) return null;

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

            var corners = new Point3d[8];
            for (int end = 0; end < 2; end++)
            {
                Point3d originPt = end == 0 ? axis.From : axis.To;
                corners[end * 4 + 0] = originPt - localY * hw - localZ * hh;
                corners[end * 4 + 1] = originPt + localY * hw - localZ * hh;
                corners[end * 4 + 2] = originPt + localY * hw + localZ * hh;
                corners[end * 4 + 3] = originPt - localY * hw + localZ * hh;
            }

            var mesh = new Mesh();
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

        // ─── Rhino viewport label drawing (Lite HUD) ──────────────────

        public override BoundingBox ClippingBox
        {
            get
            {
                if (!_previewBounds.IsValid) return base.ClippingBox;
                return _previewBounds;
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (_viewportLabels.Count == 0) return;

            foreach (var (anchor, text, color) in _viewportLabels)
            {
                args.Display.Draw3dText(text, color, new Plane(anchor, Vector3d.ZAxis), 0.5, "Arial");
            }
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("D8E7F6A5-4B3C-4A1D-8E2F-9C0B1A2D3E4F");
    }
}
