using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Forces preview from SG Assembly. Same inputs as <see cref="GI_DeformAssembly"/>.
    /// Per model outputs:
    ///   - Undeformed and Deformed lines, MaxDisp.
    ///   - Section meshes drawn on the <b>undeformed</b> lines.
    ///   - Three diagram meshes per element (one quad per element, extruded in global Z):
    ///     Fx (axial N), Fy (shear Vy), My (bending around y).
    ///     Vertices: { startPt, startPt+Z*startVal*scale, endPt+Z*endVal*scale, endPt }.
    ///   - Reaction forces and moments per support: points + force vector (kN) + moment vector (kNm).
    /// </summary>
    public class GI_ForcesAssembly : GH_Component
    {
        public GI_ForcesAssembly()
          : base("GI_Forces (Assembly)", "GI_For_A",
              "Per-element force/moment diagrams (Fx, Fy, My) and support reactions from an SG Assembly.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override bool IsPreviewCapable => true;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_FromSg", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). Leave empty or -1 = all (default).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual(s). Leave empty or -1 = all (default).", GH_ParamAccess.list, -1);
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between rows. Default (0, 0, -10).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingCompact);
            pManager.AddNumberParameter("Scale", "Scale", "Deformation scale factor (1.0 = true scale)", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Load Case", "LC", "Load case index (-1 = last available)", GH_ParamAccess.item, -1);
            pManager.AddNumberParameter("Util Ranges", "URng", "Unused here (kept for parity with GI_Deform)", GH_ParamAccess.list);
            pManager.AddPointParameter("Insert Point", "Pt", "Base point", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Text Height", "TxH", "Unused here (kept for parity with GI_Deform)", GH_ParamAccess.item, 0.3);
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Show only top N best per (generation, cluster). 0 = use Top % only (default with Top%=1 shows all).", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Top %", "Top%", "Fraction of best individuals per (generation, cluster) when TopN=0. 1 = all (default).", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Diagram Scale", "DScl", "Force/moment diagram scale: 1 unit (kN or kNm) → DScl meters in global Z. Default 0.01.", GH_ParamAccess.item, 0.01);
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each cell's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
            pManager[13].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Undeformed", "Undef", "Undeformed lines per model", GH_ParamAccess.tree);
            pManager.AddLineParameter("Deformed", "Def", "Deformed lines per model", GH_ParamAccess.tree);
            pManager.AddNumberParameter("MaxDisp", "MaxD", "Maximum displacement per model", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Section Meshes", "SecM", "Section extrusion meshes drawn on undeformed lines", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Fx Diagram", "Fx", "Axial force diagram meshes per element (kN, extruded in global Z)", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Fy Diagram", "Fy", "Shear Vy diagram meshes per element (kN, extruded in global Z)", GH_ParamAccess.tree);
            pManager.AddMeshParameter("My Diagram", "My", "Bending moment My diagram meshes per element (kNm, extruded in global Z)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Reaction Pts", "RPt", "Support points per model", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Reaction Forces", "RF", "Reaction force vector (FX, FY, FZ) in kN per support", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Reaction Moments", "RM", "Reaction moment vector (MX, MY, MZ) in kNm per support", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_SGAssembly ghAssembly = null;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly required.");
                return;
            }
            var assembly = ghAssembly.Value;

            List<int> genList = new List<int>();
            List<int> indList = new List<int>();
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingCompact;
            double scale = 1.0;
            int lcIndex = -1;
            Point3d insertPt = Point3d.Origin;
            DA.GetData(3, ref colSpacing);
            DA.GetData(4, ref rowSpacing);
            DA.GetData(5, ref scale);
            DA.GetData(6, ref lcIndex);
            // 7: Util Ranges (unused, kept for parity)
            DA.GetData(8, ref insertPt);
            // 9: Text Height (unused, kept for parity)
            int topN = 0;
            double topPercent = 1.0;
            DA.GetData(10, ref topN);
            DA.GetData(11, ref topPercent);
            double diagramScale = 0.01;
            DA.GetData(12, ref diagramScale);

            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 13);

            topPercent = Math.Clamp(topPercent, 0, 1.0);
            topN = Math.Max(0, topN);

            const double topFracEps = 1e-12;
            bool useEliteFilter = topN > 0 || topPercent < 1.0 - topFracEps;
            int maxPerCluster = MaxIndividualsPerClusterInAnyGeneration(assembly);
            if (useEliteFilter && maxPerCluster <= 1 && (topN > 1 || topPercent < 1.0 - topFracEps))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "This assembly has at most one individual per (generation, cluster). Top N and Top % need several per cluster " +
                    "(e.g. use GI_FromSg, or GI_Large input 'Asm K/Clust' > 1).");
            }

            var eliteSet = new HashSet<(int gen, int ind)>();
            if (useEliteFilter)
            {
                var candidates = new List<(int gen, int ind, double fit, int clust)>();
                foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
                {
                    if (!allGens && !genList.Contains(gen.Generation)) continue;
                    for (int indIdx = 0; indIdx < (gen.Individuals?.Count ?? 0); indIdx++)
                    {
                        if (!allInds && indSet != null && !indSet.Contains(indIdx)) continue;
                        var ind = gen.Individuals[indIdx];
                        if (ind == null || ind.Fitness >= double.MaxValue * 0.5) continue;
                        candidates.Add((gen.Generation, indIdx, ind.Fitness, ind.ClustGrp));
                    }
                }
                foreach (var grp in candidates.GroupBy(c => (c.gen, c.clust)))
                {
                    var ordered = grp.OrderBy(c => c.fit).ToList();
                    int keep = topN > 0
                        ? Math.Min(topN, ordered.Count)
                        : Math.Max(1, (int)Math.Ceiling(ordered.Count * topPercent));
                    for (int k = 0; k < keep && k < ordered.Count; k++)
                        eliteSet.Add((ordered[k].gen, ordered[k].ind));
                }
            }

            var undefTree = new GH_Structure<GH_Line>();
            var defTree = new GH_Structure<GH_Line>();
            var maxDispTree = new GH_Structure<GH_Number>();
            var secMeshTree = new GH_Structure<GH_Mesh>();
            var fxMeshTree = new GH_Structure<GH_Mesh>();
            var fyMeshTree = new GH_Structure<GH_Mesh>();
            var myMeshTree = new GH_Structure<GH_Mesh>();
            var reactPtTree = new GH_Structure<GH_Point>();
            var reactFTree = new GH_Structure<GH_Vector>();
            var reactMTree = new GH_Structure<GH_Vector>();

            // Diagram colours: blue (axial), green (shear), orange (bending).
            Color colFx = Color.FromArgb(160, 30, 100, 220);
            Color colFy = Color.FromArgb(160, 30, 180, 90);
            Color colMy = Color.FromArgb(160, 240, 140, 30);

            int col = 0;
            int totalModels = 0;
            foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
            {
                if (!allGens && !genList.Contains(gen.Generation)) continue;
                int row = 0;
                for (int indIdx = 0; indIdx < (gen.Individuals?.Count ?? 0); indIdx++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(indIdx))) continue;
                    if (useEliteFilter && !eliteSet.Contains((gen.Generation, indIdx))) continue;
                    var ind = gen.Individuals[indIdx];
                    TB_Model model = ind?.Model;
                    if (model == null || model.Elem1Ds == null || model.Nodes == null) { row++; continue; }

                    Point3d cellOrigin = insertPt + col * colSpacing + row * rowSpacing;
                    Vector3d offset = (Vector3d)cellOrigin;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform3D(displayPlane, cellOrigin);
                    GH_Path outPath = new GH_Path(col, row);

                    int resolvedLC = ResolveLoadCase(model, lcIndex);
                    bool hasDisps = resolvedLC >= 0;
                    double maxDisp = 0;

                    var nodeDefPts = new Dictionary<int, Point3d>();
                    if (hasDisps)
                    {
                        foreach (var node in model.Nodes)
                        {
                            if (node.Id == null) continue;
                            Point3d defPt = node.Pt;
                            if (node.Disps != null && resolvedLC < node.Disps.Count)
                            {
                                double[] d = node.Disps[resolvedLC];
                                Vector3d disp = new Vector3d(d[0] * scale, d[1] * scale, d[2] * scale);
                                defPt = node.Pt + disp;
                                double mag = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                                if (mag > maxDisp) maxDisp = mag;
                            }
                            nodeDefPts[node.Id.Value] = defPt;
                        }
                    }

                    foreach (var elem in model.Elem1Ds)
                    {
                        if (elem.Nodes == null || elem.Nodes.Count < 2) continue;
                        var n0 = elem.Nodes[0];
                        var n1 = elem.Nodes[1];
                        if (n0 == null || n1 == null) continue;

                        Point3d p0 = n0.Pt + offset;
                        Point3d p1 = n1.Pt + offset;
                        Line undef = new Line(p0, p1);
                        Line undefXf = undef;
                        undefXf.Transform(cellXf);
                        undefTree.Append(new GH_Line(undefXf), outPath);

                        Line defLine;
                        if (hasDisps && n0.Id != null && n1.Id != null
                            && nodeDefPts.ContainsKey(n0.Id.Value) && nodeDefPts.ContainsKey(n1.Id.Value))
                        {
                            defLine = new Line(
                                nodeDefPts[n0.Id.Value] + offset,
                                nodeDefPts[n1.Id.Value] + offset);
                            defLine.Transform(cellXf);
                        }
                        else defLine = undefXf;
                        defTree.Append(new GH_Line(defLine), outPath);

                        // Section mesh on the undeformed line
                        double sw = 0, sh = 0;
                        if (elem.Sec is Section_RHS rhs) { sw = rhs.W; sh = rhs.H; }
                        else if (elem.Sec is Section_Rect rect) { sw = rect.B; sh = rect.H; }
                        if (sw > 0 && sh > 0)
                        {
                            Mesh m = ExtrudeRect(undefXf, sw * 0.001, sh * 0.001, Color.FromArgb(180, 200, 200, 200));
                            if (m != null) secMeshTree.Append(new GH_Mesh(m), outPath);
                        }

                        // Force/moment diagrams — require disps to compute element forces
                        if (hasDisps && n0.Disps != null && n1.Disps != null
                            && resolvedLC < n0.Disps.Count && resolvedLC < n1.Disps.Count)
                        {
                            double[] F;
                            try { F = elem.Calc_Forces(resolvedLC); }
                            catch { F = null; }

                            if (F != null && F.Length >= 12)
                            {
                                // Sign convention follows ST_Elem1DForces:
                                //   start values are negated, end values keep their sign.
                                // Forces N → kN, moments N·m → kN·m via 1e-3.
                                double nStart  = -F[0]  * 1e-3;
                                double nEnd    =  F[6]  * 1e-3;
                                double vyStart = -F[1]  * 1e-3;
                                double vyEnd   =  F[7]  * 1e-3;
                                double myStart = -F[4]  * 1e-3;
                                double myEnd   =  F[10] * 1e-3;

                                Mesh meshFx = BuildDiagramQuad(p0, p1, nStart,  nEnd,  diagramScale, colFx);
                                Mesh meshFy = BuildDiagramQuad(p0, p1, vyStart, vyEnd, diagramScale, colFy);
                                Mesh meshMy = BuildDiagramQuad(p0, p1, myStart, myEnd, diagramScale, colMy);

                                if (meshFx != null) { meshFx.Transform(cellXf); fxMeshTree.Append(new GH_Mesh(meshFx), outPath); }
                                if (meshFy != null) { meshFy.Transform(cellXf); fyMeshTree.Append(new GH_Mesh(meshFy), outPath); }
                                if (meshMy != null) { meshMy.Transform(cellXf); myMeshTree.Append(new GH_Mesh(meshMy), outPath); }
                            }
                        }
                    }

                    // Reactions per support
                    if (model.Sups != null)
                    {
                        foreach (var sup in model.Sups)
                        {
                            if (sup == null) continue;
                            Point3d sPt = sup.Pt + offset;
                            sPt.Transform(cellXf);
                            reactPtTree.Append(new GH_Point(sPt), outPath);

                            Vector3d rF = Vector3d.Zero;
                            Vector3d rM = Vector3d.Zero;
                            if (resolvedLC >= 0 && sup.React != null && resolvedLC < sup.React.Count
                                && sup.React[resolvedLC] != null && sup.React[resolvedLC].Count >= 6)
                            {
                                var R = sup.React[resolvedLC];
                                rF = new Vector3d(R[0] * 1e-3, R[1] * 1e-3, R[2] * 1e-3);
                                rM = new Vector3d(R[3] * 1e-3, R[4] * 1e-3, R[5] * 1e-3);
                            }
                            rF.Transform(cellXf);
                            rM.Transform(cellXf);
                            reactFTree.Append(new GH_Vector(rF), outPath);
                            reactMTree.Append(new GH_Vector(rM), outPath);
                        }
                    }

                    maxDispTree.Append(new GH_Number(maxDisp), outPath);
                    totalModels++;
                    row++;
                }
                col++;
            }

            DA.SetDataTree(0, undefTree);
            DA.SetDataTree(1, defTree);
            DA.SetDataTree(2, maxDispTree);
            DA.SetDataTree(3, secMeshTree);
            DA.SetDataTree(4, fxMeshTree);
            DA.SetDataTree(5, fyMeshTree);
            DA.SetDataTree(6, myMeshTree);
            DA.SetDataTree(7, reactPtTree);
            DA.SetDataTree(8, reactFTree);
            DA.SetDataTree(9, reactMTree);
            DA.SetData(10, string.Format(
                "Forces from Assembly: {0} models. Diagram scale = {1} (1 kN/kNm → {1} m in Z).",
                totalModels, diagramScale));
        }

        #region Helpers

        /// <summary>
        /// Builds a quad face per element extruded in global Z by the (start, end) scalar values.
        /// Vertices order: { startPt, startPt + Z*startVal*scale, endPt + Z*endVal*scale, endPt }.
        /// Returns null if both values are ~0.
        /// </summary>
        private static Mesh BuildDiagramQuad(Point3d startPt, Point3d endPt, double startVal, double endVal, double scale, Color faceColour)
        {
            if (Math.Abs(startVal) < 1e-12 && Math.Abs(endVal) < 1e-12) return null;
            Point3d v0 = startPt;
            Point3d v1 = new Point3d(startPt.X, startPt.Y, startPt.Z + startVal * scale);
            Point3d v2 = new Point3d(endPt.X,   endPt.Y,   endPt.Z   + endVal   * scale);
            Point3d v3 = endPt;
            var mesh = new Mesh();
            mesh.Vertices.Add(v0);
            mesh.Vertices.Add(v1);
            mesh.Vertices.Add(v2);
            mesh.Vertices.Add(v3);
            mesh.VertexColors.Add(faceColour);
            mesh.VertexColors.Add(faceColour);
            mesh.VertexColors.Add(faceColour);
            mesh.VertexColors.Add(faceColour);
            mesh.Faces.AddFace(0, 1, 2, 3);
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        private static int MaxIndividualsPerClusterInAnyGeneration(SGShapeGrammar3DAssembly assembly)
        {
            int m = 0;
            foreach (var gen in assembly.Generations ?? Enumerable.Empty<AssemblyGeneration>())
            {
                if (gen?.Individuals == null || gen.Individuals.Count == 0) continue;
                foreach (var grp in gen.Individuals.GroupBy(ind => ind.ClustGrp))
                    m = Math.Max(m, grp.Count());
            }
            return m;
        }

        private static int ResolveLoadCase(TB_Model model, int requested)
        {
            if (model.Nodes == null || model.Nodes.Count == 0) return -1;
            var first = model.Nodes.FirstOrDefault(n => n.Disps != null && n.Disps.Count > 0);
            if (first == null) return -1;
            int num = first.Disps.Count;
            return (requested < 0 || requested >= num) ? num - 1 : requested;
        }

        private static Mesh ExtrudeRect(Line axis, double wM, double hM, Color faceColour)
        {
            if (wM <= 0 || hM <= 0 || axis.Length < 1e-12) return null;
            double hw = wM * 0.5, hh = hM * 0.5;
            Vector3d t = axis.UnitTangent;
            Vector3d ly = Math.Abs(t * Vector3d.ZAxis) > 0.99 ? Vector3d.YAxis : Vector3d.CrossProduct(Vector3d.ZAxis, t);
            ly.Unitize();
            Vector3d lz = Vector3d.CrossProduct(t, ly);
            lz.Unitize();
            Point3d[] c = new Point3d[8];
            for (int end = 0; end < 2; end++)
            {
                Point3d o = end == 0 ? axis.From : axis.To;
                c[end * 4 + 0] = o - ly * hw - lz * hh;
                c[end * 4 + 1] = o + ly * hw - lz * hh;
                c[end * 4 + 2] = o + ly * hw + lz * hh;
                c[end * 4 + 3] = o - ly * hw + lz * hh;
            }
            int[][] faces = new int[][] {
                new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 },
                new[] { 3, 0, 4, 7 }, new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 } };
            var mesh = new Mesh();
            int vi = 0;
            foreach (var f in faces)
            {
                mesh.Vertices.Add(c[f[0]]); mesh.Vertices.Add(c[f[1]]); mesh.Vertices.Add(c[f[2]]); mesh.Vertices.Add(c[f[3]]);
                mesh.VertexColors.Add(faceColour); mesh.VertexColors.Add(faceColour); mesh.VertexColors.Add(faceColour); mesh.VertexColors.Add(faceColour);
                mesh.Faces.AddFace(vi, vi + 1, vi + 2, vi + 3);
                vi += 4;
            }
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        #endregion

        protected override Bitmap Icon => Properties.Resources.icons_CAT_DataPreview;
        public override Guid ComponentGuid => new Guid("D8A1E5C2-4F92-4B61-9E3D-3F1A6C77B011");
    }
}
