using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Preview topology and shape metrics from Assembly. Inputs: Assembly, Individual, X/Y spacing (like other previewers).
    /// Outputs: scores (text), node geometry, line geometry, mesh geometry (area from lines).
    /// </summary>
    public class GI_MetricsPreview : GH_Component
    {
        public GI_MetricsPreview()
          : base("GI_Metrics (Assembly)", "GI_Metrics",
              "Preview topology and shape metrics from Assembly. Outputs: scores, node pts, lines, area mesh (from lines).",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        public override Guid ComponentGuid => new Guid("A7F2E9B1-4C3D-5E6F-8A9B-0C1D2E3F4A5B");

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Asm",
                "GA output bundle from GI_FromSg / GI_LargeSg containing evaluated individuals.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation index filter (-1 lists every generation stored in the assembly).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual index filter inside each generation (-1 keeps full population).", GH_ParamAccess.list, -1);
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between rows. Default (0, 0, -10).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingCompact);
            pManager.AddPointParameter("Layout origin", "Pt",
                "Anchor point for the preview grid (optional).", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each cell's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Score tree", "Scores",
                "Tree of textual lines summarizing topology/shape metrics for each previewed structure.", GH_ParamAccess.tree);
            pManager.AddPointParameter("Nodes", "Nodes",
                "Grasshopper point tree mirroring node locations (for counting / overlays).", GH_ParamAccess.tree);
            pManager.AddLineParameter("Members", "Ln",
                "Line tree mirroring member axes for the same layout slots.", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Metric mesh", "Mesh",
                "Optional mesh derived from the line network to support area-based shape metrics.", GH_ParamAccess.tree);
            pManager.AddTextParameter("Summary", "Info",
                "High-level statistics about how many structures were processed.", GH_ParamAccess.item);
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

            List<int> genList = new List<int>(), indList = new List<int>();
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingCompact;
            Point3d insertPt = Point3d.Origin;
            DA.GetData(3, ref colSpacing);
            DA.GetData(4, ref rowSpacing);
            DA.GetData(5, ref insertPt);

            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 6);

            var gens = assembly.Generations ?? new List<AssemblyGeneration>();
            if (gens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                return;
            }

            var selectedGens = allGens ? gens.Select(g => g.Generation).OrderBy(g => g).ToList() : genList.Where(g => g >= 0 && gens.Any(gn => gn.Generation == g)).Distinct().OrderBy(g => g).ToList();
            if (selectedGens.Count == 0) selectedGens.Add(gens.Last().Generation);

            var scoresTree = new GH_Structure<GH_String>();
            var nodeTree = new GH_Structure<GH_Point>();
            var lineTree = new GH_Structure<GH_Line>();
            var meshTree = new GH_Structure<GH_Mesh>();

            int col = 0;
            int totalCount = 0;

            foreach (var gen in gens.Where(g => selectedGens.Contains(g.Generation)))
            {
                var individuals = gen.Individuals ?? new List<AssemblyIndividual>();
                for (int row = 0; row < individuals.Count; row++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(row))) continue;
                    var ind = individuals[row];
                    var shape = ind?.Shape;
                    if (shape == null || shape.Elems == null) continue;

                    shape.RegisterElemsToNodes();

                    Point3d cellOrigin = insertPt + col * colSpacing + row * rowSpacing;
                    Vector3d offset = (Vector3d)cellOrigin;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform3D(displayPlane, cellOrigin);

                    GH_Path path = new GH_Path(col, row);

                    var scoreLines = new List<string>();
                    for (int t = 0; t < TopologyMetrics.Count; t++)
                    {
                        double val = TopologyMetrics.Compute(shape, t);
                        scoreLines.Add(string.Format("{0}: {1:F3}", TopologyMetrics.GetLabel(t), val));
                    }
                    for (int s = 0; s < ShapeMetrics.Count; s++)
                    {
                        double val = ShapeMetrics.Compute(shape, s);
                        scoreLines.Add(string.Format("{0}: {1:F3}", ShapeMetrics.GetLabel(s), val));
                    }
                    scoresTree.AppendRange(scoreLines.Select(l => new GH_String(l)), path);

                    if (shape.Nodes != null)
                    {
                        foreach (var node in shape.Nodes)
                        {
                            if (node != null)
                            {
                                Point3d p = node.Pt + offset;
                                p.Transform(cellXf);
                                nodeTree.Append(new GH_Point(p), path);
                            }
                        }
                    }

                    foreach (var elem in shape.Elems)
                    {
                        if (elem is SG_Elem1D e1d && e1d.Ln.IsValid)
                        {
                            var ln = new Line(e1d.Ln.From + offset, e1d.Ln.To + offset);
                            ln.Transform(cellXf);
                            lineTree.Append(new GH_Line(ln), path);
                        }
                    }

                    var (area, mesh) = ShapeMetrics.MeshAreaFromLinesWithMesh(shape);
                    if (mesh != null && mesh.IsValid)
                    {
                        mesh.Translate(offset);
                        mesh.Transform(cellXf);
                        meshTree.Append(new GH_Mesh(mesh), path);
                    }

                    totalCount++;
                }
                col++;
            }

            DA.SetDataTree(0, scoresTree);
            DA.SetDataTree(1, nodeTree);
            DA.SetDataTree(2, lineTree);
            DA.SetDataTree(3, meshTree);
            DA.SetData(4, string.Format("Metrics Preview: {0} structures. Topo: {1}, Shape: {2} metrics.", totalCount, TopologyMetrics.Count, ShapeMetrics.Count));
        }
    }
}
