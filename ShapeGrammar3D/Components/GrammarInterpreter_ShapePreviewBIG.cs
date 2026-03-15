using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
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
    /// Big preview: SG_Shape with deformation, section meshes (gray + utilization-coloured),
    /// utilization labels, and a single summary string per structure for text tags.
    /// Expects Shapes and Models as data trees {generation}(individual). Optional Cluster tree for colours.
    /// </summary>
    public class GI_ShapePreviewBIG : GH_Component
    {
        private struct DotMark { public Point3d Position; public Color Colour; }
        private List<DotMark> _angleDots = new List<DotMark>();
        private List<DotMark> _intersectDots = new List<DotMark>();
        private List<DotMark> _danglingDots = new List<DotMark>();
        private double _dotRadius = 0.15;

        public GI_ShapePreviewBIG()
          : base("GI_ShapePreviewBIG", "GI_ShapeBIG",
              "Preview SG_Shape with undeformed/deformed lines and nodes, section meshes (gray + util), util labels, and summary string per structure for text tags.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            float r = (float)_dotRadius;
            foreach (var d in _angleDots)
                args.Display.DrawSphere(new Sphere(d.Position, r), d.Colour);
            foreach (var d in _intersectDots)
                args.Display.DrawSphere(new Sphere(d.Position, r), d.Colour);
            foreach (var d in _danglingDots)
                args.Display.DrawSphere(new Sphere(d.Position, r), d.Colour);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Shapes", "Shapes", "SG_Shape data tree {generation}(individual). From GI_Auto7.", GH_ParamAccess.tree);
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models", "TB_Model data tree {generation}(individual). Same structure as Shapes.", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Cluster", "Cluster", "Cluster index per structure (same path as Shapes). Optional; for colouring.", GH_ParamAccess.tree);
            pManager.AddColourParameter("Colours", "Col", "Colour per structure (same path as Shapes). Optional; overrides cluster colour.", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Show Mesh", "Mesh", "Section extrusion meshes", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Show Utilization", "Util", "Colour second mesh by utilization (requires Models)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Show Angle", "Angle", "Feasibility: show angle nodes", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Show Intersection", "Intersect", "Feasibility: show intersections", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Show Dangling", "Dangling", "Feasibility: show dangling", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Deformed", "Def", "Use deformed geometry when Models have displacements", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Deform Scale", "Scale", "Scale factor for displacement display", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Load Case", "LC", "Load case for utilization (-1 = last)", GH_ParamAccess.item, -1);
            pManager.AddNumberParameter("X Spacing", "dX", "Horizontal spacing between columns", GH_ParamAccess.item, 30.0);
            pManager.AddNumberParameter("Y Spacing", "dY", "Vertical spacing between rows", GH_ParamAccess.item, 10.0);
            pManager.AddPointParameter("Insert Point", "Pt", "Base point for layout", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Dot Radius", "DotR", "Radius for angle/intersection/dangling dots", GH_ParamAccess.item, 0.15);
            pManager.AddNumberParameter("Fitness", "Fitness", "Fitness per structure (tree, same path). Optional; for summary.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Topo", "Topo", "Topological metrics per structure (tree, same path). Optional; for summary.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Shpe", "Shpe", "Shape metrics per structure (tree, same path). Optional; for summary.", GH_ParamAccess.tree);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
            pManager[13].Optional = true;
            pManager[14].Optional = true;
            pManager[15].Optional = true;
            pManager[16].Optional = true;
            pManager[17].Optional = true;
            pManager[18].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Undeformed Lines", "UndefLn", "Undeformed element lines {gen}(ind)(element)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Undeformed Nodes", "UndefPt", "Undeformed node points {gen}(ind)(node)", GH_ParamAccess.tree);
            pManager.AddLineParameter("Deformed Lines", "DefLn", "Deformed element lines {gen}(ind)(element)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Deformed Nodes", "DefPt", "Deformed node points {gen}(ind)(node)", GH_ParamAccess.tree);
            pManager.AddTextParameter("Deformation", "DispTxt", "Deformation summary per structure for text tag (e.g. max displacement)", GH_ParamAccess.tree);
            pManager.AddTextParameter("Summary", "Summary", "One descriptive string per structure: Individual, gen, max disp, util %, VDang, VAng, VIntersect, FeasTotal, Topo/Shpe. For text tag.", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Mesh", "Mesh", "Section meshes (gray, no utilization) {gen}(ind)(element)", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Mesh Util", "MeshU", "Section meshes coloured by utilization {gen}(ind)(element)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Util Label Pts", "UtilPt", "Point at mid-element for utilization text tag {gen}(ind)(element)", GH_ParamAccess.tree);
            pManager.AddTextParameter("Util Label Txt", "UtilTxt", "Utilization % per element for text tag {gen}(ind)(element)", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _angleDots.Clear();
            _intersectDots.Clear();
            _danglingDots.Clear();

            GH_Structure<IGH_Goo> shapesTree = new GH_Structure<IGH_Goo>();
            if (!DA.GetDataTree(0, out shapesTree)) return;

            GH_Structure<GH_TB_Model> modelsTree = new GH_Structure<GH_TB_Model>();
            DA.GetDataTree(1, out modelsTree);
            bool hasModels = modelsTree != null && modelsTree.DataCount > 0;

            GH_Structure<GH_Integer> clusterTree = new GH_Structure<GH_Integer>();
            DA.GetDataTree(2, out clusterTree);
            GH_Structure<GH_Colour> coloursTree = new GH_Structure<GH_Colour>();
            DA.GetDataTree(3, out coloursTree);
            bool hasColours = coloursTree != null && coloursTree.DataCount > 0;

            bool showMesh = true, showUtil = true, showAngle = false, showIntersect = false, showDangling = false;
            bool useDeformed = true;
            double deformScale = 1.0;
            int lcIndex = -1;
            double xSpacing = 30.0, ySpacing = 10.0;
            Point3d insertPt = Point3d.Origin;
            double dotR = 0.15;

            DA.GetData(4, ref showMesh);
            DA.GetData(5, ref showUtil);
            DA.GetData(6, ref showAngle);
            DA.GetData(7, ref showIntersect);
            DA.GetData(8, ref showDangling);
            DA.GetData(9, ref useDeformed);
            DA.GetData(10, ref deformScale);
            DA.GetData(11, ref lcIndex);
            DA.GetData(12, ref xSpacing);
            DA.GetData(13, ref ySpacing);
            DA.GetData(14, ref insertPt);
            DA.GetData(15, ref dotR);
            _dotRadius = Math.Max(0.01, dotR);

            GH_Structure<GH_Number> fitnessTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> topoTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> shpeTree = new GH_Structure<GH_Number>();
            DA.GetDataTree(16, out fitnessTree);
            DA.GetDataTree(17, out topoTree);
            DA.GetDataTree(18, out shpeTree);
            bool hasFitness = fitnessTree != null && fitnessTree.DataCount > 0;
            bool hasTopo = topoTree != null && topoTree.DataCount > 0;
            bool hasShpe = shpeTree != null && shpeTree.DataCount > 0;

            var feasSettings = FeasibilitySettings.Default();
            double[] utilThresholds = new double[] { 50, 80, 95, 100, 110 };

            var undefLinesTree = new GH_Structure<GH_Line>();
            var undefNodesTree = new GH_Structure<GH_Point>();
            var defLinesTree = new GH_Structure<GH_Line>();
            var defNodesTree = new GH_Structure<GH_Point>();
            var dispTxtTree = new GH_Structure<GH_String>();
            var summaryTree = new GH_Structure<GH_String>();
            var meshGrayTree = new GH_Structure<GH_Mesh>();
            var meshUtilTree = new GH_Structure<GH_Mesh>();
            var utilLabelPtsTree = new GH_Structure<GH_Point>();
            var utilLabelTxtTree = new GH_Structure<GH_String>();

            int totalCount = 0;
            int numClusters = 1;
            for (int b = 0; b < shapesTree.PathCount; b++)
            {
                GH_Path path = shapesTree.Paths[b];
                var shapeBranch = shapesTree.Branches[b];
                List<GH_TB_Model> modelBranch = hasModels && modelsTree.PathExists(path) ? (List<GH_TB_Model>)modelsTree.get_Branch(path) : null;
                List<GH_Integer> clusterBranch = clusterTree != null && clusterTree.PathExists(path) ? (List<GH_Integer>)clusterTree.get_Branch(path) : null;
                List<GH_Colour> colBranch = hasColours && coloursTree.PathExists(path) ? (List<GH_Colour>)coloursTree.get_Branch(path) : null;

                for (int i = 0; i < shapeBranch.Count; i++)
                {
                    SG_Shape shape = ExtractShape(shapeBranch[i] as IGH_Goo);
                    if (shape == null) continue;

                    TB_Model model = null;
                    if (modelBranch != null && i < modelBranch.Count && modelBranch[i] != null)
                        model = modelBranch[i].Value;

                    int clusterIdx = 0;
                    if (clusterBranch != null && i < clusterBranch.Count && clusterBranch[i] != null)
                    {
                        clusterIdx = clusterBranch[i].Value;
                        if (clusterIdx >= numClusters) numClusters = clusterIdx + 1;
                    }
                    Color structColor = Color.Gray;
                    if (colBranch != null && i < colBranch.Count && colBranch[i] != null)
                        structColor = colBranch[i].Value;
                    else if (clusterBranch != null)
                        structColor = GetClusterColour(clusterIdx, numClusters);

                    int genIdxPath = path.Length > 0 ? path[0] : b;
                    GH_Path outPath = new GH_Path(genIdxPath, i);
                    Vector3d offset = new Vector3d(
                        insertPt.X + b * xSpacing,
                        insertPt.Y - i * ySpacing,
                        insertPt.Z);

                    shape.RegisterElemsToNodes();
                    FeasibilityResult feas = FeasibilityMetrics.Compute(shape, feasSettings);

                    bool useDeformedGeom = useDeformed && model != null && model.Nodes != null;
                    var nodeDefPts = new Dictionary<int, Point3d>();
                    double maxDisp = 0;
                    if (useDeformedGeom)
                    {
                        int lc = ResolveLoadCase(model, lcIndex);
                        if (lc >= 0)
                        {
                            foreach (var node in model.Nodes)
                            {
                                if (node.Id == null) continue;
                                Point3d defPt = node.Pt;
                                if (node.Disps != null && lc < node.Disps.Count)
                                {
                                    double[] d = node.Disps[lc];
                                    defPt = node.Pt + new Vector3d(d[0] * deformScale, d[1] * deformScale, d[2] * deformScale);
                                    double mag = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                                    if (mag > maxDisp) maxDisp = mag;
                                }
                                nodeDefPts[node.Id.Value] = defPt;
                            }
                        }
                        else
                            useDeformedGeom = false;
                    }

                    double maxUtil = 0, sumUtil = 0;
                    int utilCount = 0;
                    int lcUtil = (showUtil && model != null) ? ResolveLoadCase(model, lcIndex) : -1;

                    if (shape.Nodes != null)
                    {
                        for (int nIdx = 0; nIdx < shape.Nodes.Count; nIdx++)
                        {
                            var node = shape.Nodes[nIdx];
                            Point3d undefPt = node.Pt + offset;
                            undefNodesTree.Append(new GH_Point(undefPt), outPath);
                            if (useDeformedGeom && model?.Nodes != null && nIdx < model.Nodes.Count)
                            {
                                var mn = model.Nodes[nIdx];
                                if (mn.Id != null && nodeDefPts.TryGetValue(mn.Id.Value, out Point3d defPt))
                                    defNodesTree.Append(new GH_Point(defPt + offset), outPath);
                                else
                                    defNodesTree.Append(new GH_Point(undefPt), outPath);
                            }
                            else
                                defNodesTree.Append(new GH_Point(undefPt), outPath);
                        }
                    }

                    if (shape.Elems != null)
                    {
                        foreach (SG_Element elem in shape.Elems)
                        {
                            if (!(elem is SG_Elem1D elem1d)) continue;

                            Line ln = GetElementLine(elem1d);
                            if (!ln.IsValid) continue;

                            TB_Element_1D modelElem = FindModelElementByLine(model, ln);
                            Line undefLn = new Line(ln.From + offset, ln.To + offset);
                            Line displayLn = ln;
                            if (useDeformedGeom && modelElem?.Nodes != null && modelElem.Nodes.Count >= 2
                                && modelElem.Nodes[0].Id != null && modelElem.Nodes[1].Id != null
                                && nodeDefPts.TryGetValue(modelElem.Nodes[0].Id.Value, out Point3d p0)
                                && nodeDefPts.TryGetValue(modelElem.Nodes[1].Id.Value, out Point3d p1))
                                displayLn = new Line(p0, p1);
                            displayLn = new Line(displayLn.From + offset, displayLn.To + offset);

                            undefLinesTree.Append(new GH_Line(undefLn), outPath);
                            defLinesTree.Append(new GH_Line(displayLn), outPath);

                            double util = 0;
                            if (modelElem != null && lcUtil >= 0)
                            {
                                util = ComputeUtilization(model, modelElem, lcUtil);
                                if (util > maxUtil) maxUtil = util;
                                sumUtil += util;
                                utilCount++;
                            }

                            double secW = 0, secH = 0;
                            if (modelElem?.Sec is Section_RHS rhsSec) { secW = rhsSec.W; secH = rhsSec.H; }
                            else if (modelElem?.Sec is Section_Rect rect) { secW = rect.B; secH = rect.H; }
                            else if (elem1d.CrossSection is SH_CrossSection_RHS shRhs) { secW = shRhs.Width; secH = shRhs.Height; }
                            else if (elem1d.CrossSection is SH_CrossSection_Rectangle shRect) { secW = shRect.width; secH = shRect.height; }
                            if (secW <= 0 || secH <= 0) { secW = 0.05; secH = 0.05; }

                            if (showMesh)
                            {
                                Mesh mGray = ExtrudeRectSection(displayLn, secW * 0.001, secH * 0.001, Color.FromArgb(180, Color.Gray));
                                if (mGray != null) meshGrayTree.Append(new GH_Mesh(mGray), outPath);

                                Color meshColor = showUtil && modelElem != null && lcUtil >= 0 ? UtilColor(util * 100.0, utilThresholds) : structColor;
                                Mesh mUtil = ExtrudeRectSection(displayLn, secW * 0.001, secH * 0.001, Color.FromArgb(180, meshColor));
                                if (mUtil != null) meshUtilTree.Append(new GH_Mesh(mUtil), outPath);
                            }

                            Point3d midPt = displayLn.PointAt(0.5);
                            utilLabelPtsTree.Append(new GH_Point(midPt), outPath);
                            utilLabelTxtTree.Append(new GH_String(string.Format("{0:F1}%", util * 100.0)), outPath);
                        }
                    }

                    double avgUtil = utilCount > 0 ? sumUtil / utilCount : 0;
                    string dispTxt = string.Format("Max displacement: {0:F4} mm", maxDisp);
                    dispTxtTree.Append(new GH_String(dispTxt), outPath);

                    string topoStr = "";
                    if (hasTopo && topoTree.PathExists(outPath))
                    {
                        var topoBranch = (List<GH_Number>)topoTree.get_Branch(outPath);
                        if (topoBranch != null && topoBranch.Count > 0)
                            topoStr = ", Topo: [" + string.Join(", ", topoBranch.Take(5).Select(x => x.Value.ToString("F2"))) + (topoBranch.Count > 5 ? "..." : "") + "]";
                    }
                    string shpeStr = "";
                    if (hasShpe && shpeTree.PathExists(outPath))
                    {
                        var shpeBranch = (List<GH_Number>)shpeTree.get_Branch(outPath);
                        if (shpeBranch != null && shpeBranch.Count > 0)
                            shpeStr = ", Shpe: [" + string.Join(", ", shpeBranch.Take(5).Select(x => x.Value.ToString("F2"))) + (shpeBranch.Count > 5 ? "..." : "") + "]";
                    }
                    double fitnessVal = 0;
                    if (hasFitness && fitnessTree.PathExists(new GH_Path(genIdxPath)))
                    {
                        var fitBranch = (List<GH_Number>)fitnessTree.get_Branch(new GH_Path(genIdxPath));
                        if (fitBranch != null && i < fitBranch.Count && fitBranch[i] != null)
                            fitnessVal = fitBranch[i].Value;
                    }
                    string summary = string.Format(
                        "Individual {0} in generation {1}, max displacement: {2:F4} mm, max utilization: {3:F1}%, average utilization: {4:F1}%, VDang: {5:F3}, VAng: {6:F3}, VLen: {7:F3}, VIntersect: {8:F3}, FeasTotal: {9:F3}, Fitness: {10:F4}{11}{12}",
                        i, genIdxPath, maxDisp, maxUtil * 100.0, avgUtil * 100.0, feas.VDang, feas.VAng, feas.VLen, feas.VIntersect, feas.TotalViolation, fitnessVal, topoStr, shpeStr);
                    summaryTree.Append(new GH_String(summary), outPath);

                    if (showAngle)
                    {
                        var angleData = FeasibilityMetrics.GetAllNodesAngleData(shape, feasSettings.AngleMinDeg, feasSettings.AngleOptDeg);
                        foreach (var (Node, MinAngleDeg, Classification) in angleData)
                        {
                            Color dotColor = Classification >= 0 ? FeasColor(Classification) : Color.FromArgb(180, 160, 160, 160);
                            _angleDots.Add(new DotMark { Position = Node.Pt + offset, Colour = dotColor });
                        }
                    }
                    if (showIntersect)
                    {
                        foreach (var d in FeasibilityMetrics.GetIntersectionData(shape))
                            _intersectDots.Add(new DotMark { Position = d.Point + offset, Colour = Color.FromArgb(255, 200, 0) });
                    }
                    if (showDangling)
                    {
                        foreach (var (_, MidPoint) in FeasibilityMetrics.GetDanglingElementMidpoints(shape))
                            _danglingDots.Add(new DotMark { Position = MidPoint + offset, Colour = Color.FromArgb(255, 50, 50) });
                    }

                    totalCount++;
                }
            }

            DA.SetDataTree(0, undefLinesTree);
            DA.SetDataTree(1, undefNodesTree);
            DA.SetDataTree(2, defLinesTree);
            DA.SetDataTree(3, defNodesTree);
            DA.SetDataTree(4, dispTxtTree);
            DA.SetDataTree(5, summaryTree);
            DA.SetDataTree(6, meshGrayTree);
            DA.SetDataTree(7, meshUtilTree);
            DA.SetDataTree(8, utilLabelPtsTree);
            DA.SetDataTree(9, utilLabelTxtTree);
            DA.SetData(10, string.Format(
                "GI_ShapePreviewBIG: {0} structures. Mesh: {1}, Mesh Util: {2}, Deformed: {3}.",
                totalCount, showMesh ? "ON" : "OFF", showUtil ? "ON" : "OFF", useDeformed ? "ON" : "OFF"));
        }

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0, 1);
            int r = 0, g, b;
            if (t <= 0.5) { double s = t / 0.5; g = (int)(s * 255); b = 255; }
            else { double s = (t - 0.5) / 0.5; g = 255; b = (int)((1.0 - s) * 255); }
            return Color.FromArgb(r, g, b);
        }

        private static Color FeasColor(int cls)
        {
            if (cls == FeasibilityMetrics.CLS_GOOD) return Color.FromArgb(0, 180, 80);
            if (cls == FeasibilityMetrics.CLS_ORANGE) return Color.FromArgb(255, 165, 0);
            return Color.FromArgb(220, 50, 50);
        }

        private static Color UtilColor(double pct, double[] t)
        {
            if (pct <= t[0]) return Color.FromArgb(30, 100, 255);
            if (pct <= t[1]) return Lerp(Color.FromArgb(30, 100, 255), Color.FromArgb(0, 200, 80), (pct - t[0]) / (t[1] - t[0]));
            if (pct <= t[2]) return Color.FromArgb(0, 200, 80);
            if (pct <= t[3]) return Lerp(Color.FromArgb(0, 200, 80), Color.FromArgb(255, 220, 0), (pct - t[2]) / (t[3] - t[2]));
            if (pct <= t[4]) return Lerp(Color.FromArgb(255, 220, 0), Color.FromArgb(220, 30, 30), (pct - t[3]) / (t[4] - t[3]));
            return Color.FromArgb(220, 30, 30);
        }

        private static Color Lerp(Color a, Color b, double f)
        {
            f = Math.Clamp(f, 0, 1);
            return Color.FromArgb((int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        private const double LINE_MATCH_TOL = 1e-3;
        private static Line GetElementLine(SG_Elem1D e1)
        {
            if (e1?.Nodes != null && e1.Nodes.Length >= 2 && e1.Nodes[0] != null && e1.Nodes[1] != null)
                return new Line(e1.Nodes[0].Pt, e1.Nodes[1].Pt);
            if (e1.Crv != null) return new Line(e1.Crv.PointAtStart, e1.Crv.PointAtEnd);
            return e1.Ln;
        }

        private static TB_Element_1D FindModelElementByLine(TB_Model model, Line ln)
        {
            if (model?.Elem1Ds == null || !ln.IsValid) return null;
            foreach (var e in model.Elem1Ds)
            {
                if (e?.Line == null) continue;
                if (e.Line.From.DistanceTo(ln.From) <= LINE_MATCH_TOL && e.Line.To.DistanceTo(ln.To) <= LINE_MATCH_TOL) return e;
                if (e.Line.From.DistanceTo(ln.To) <= LINE_MATCH_TOL && e.Line.To.DistanceTo(ln.From) <= LINE_MATCH_TOL) return e;
            }
            return null;
        }

        private static int ResolveLoadCase(TB_Model model, int requested)
        {
            if (model?.Nodes == null) return -1;
            var first = model.Nodes.FirstOrDefault(n => n.Disps != null && n.Disps.Count > 0);
            if (first == null) return -1;
            int num = first.Disps.Count;
            return (requested < 0 || requested >= num) ? num - 1 : requested;
        }

        private static double ComputeUtilization(TB_Model model, TB_Element_1D e, int lcIdx)
        {
            if (model == null || e?.Sec == null) return 0;
            double gM0 = 1.0;
            double N_Rd = e.Sec.Area * e.Sec.Mat.Fy / gM0;
            double My_Rd = e.Sec.Wy * e.Sec.Mat.Fy / gM0 * 1e-3;
            double Mz_Rd = e.Sec.Wz * e.Sec.Mat.Fy / gM0 * 1e-3;
            if (N_Rd <= 0 || My_Rd <= 0 || Mz_Rd <= 0) return 0;
            if (model.LCs == null || lcIdx < 0) return 0;
            int id = Math.Min(lcIdx, model.LCs.Length - 1);
            double[] F = e.Calc_Forces(id);
            double N_Ed = Math.Max(Math.Abs(F[0]), Math.Abs(F[6]));
            double My_Ed = Math.Max(Math.Abs(F[4]), Math.Abs(F[10]));
            double Mz_Ed = Math.Max(Math.Abs(F[5]), Math.Abs(F[11]));
            return N_Ed / N_Rd + My_Ed / My_Rd + Mz_Ed / Mz_Rd;
        }

        private static Mesh ExtrudeRectSection(Line axis, double widthM, double heightM, Color faceColour)
        {
            if (widthM <= 0 || heightM <= 0 || axis.Length < 1e-12) return null;
            double hw = widthM * 0.5, hh = heightM * 0.5;
            Vector3d t = axis.UnitTangent;
            Vector3d ly = Math.Abs(t * Vector3d.ZAxis) > 0.99 ? Vector3d.YAxis : Vector3d.CrossProduct(Vector3d.ZAxis, t);
            ly.Unitize();
            Vector3d lz = Vector3d.CrossProduct(t, ly);
            lz.Unitize();
            var mesh = new Mesh();
            for (int end = 0; end < 2; end++)
            {
                Point3d o = end == 0 ? axis.From : axis.To;
                mesh.Vertices.Add(o - ly * hw - lz * hh);
                mesh.Vertices.Add(o + ly * hw - lz * hh);
                mesh.Vertices.Add(o + ly * hw + lz * hh);
                mesh.Vertices.Add(o - ly * hw + lz * hh);
                for (int i = 0; i < 4; i++) mesh.VertexColors.Add(faceColour);
            }
            int[][] faces = { new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 }, new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 } };
            foreach (var f in faces) mesh.Faces.AddFace(f[0], f[1], f[2], f[3]);
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        private static SG_Shape ExtractShape(IGH_Goo goo)
        {
            if (goo == null) return null;
            if (goo is GH_ObjectWrapper wrapper) return wrapper.Value as SG_Shape;
            SG_Shape shape = null;
            goo.CastTo(out shape);
            return shape;
        }

        protected override Bitmap Icon => Properties.Resources.icons_Generic;
        public override Guid ComponentGuid => new Guid("B2C3D4E5-6F7A-8B9C-0D1E-2F3A4B5C6D7E");
    }
}
