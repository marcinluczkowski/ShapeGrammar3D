using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
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

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_FromSg", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all.", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual(s). -1 = all.", GH_ParamAccess.list, -1);
            pManager.AddNumberParameter("X Spacing", "dX", "Horizontal spacing between individuals", GH_ParamAccess.item, 30.0);
            pManager.AddNumberParameter("Y Spacing", "dY", "Vertical spacing between individuals", GH_ParamAccess.item, 10.0);
            pManager.AddPointParameter("Insert Point", "Pt", "Base point for layout", GH_ParamAccess.item, Point3d.Origin);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Scores", "Scores", "All topology and shape metric scores per individual (metric name: value)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Node Pts", "Nodes", "Node geometry (for node count)", GH_ParamAccess.tree);
            pManager.AddLineParameter("Lines", "Lines", "Element line geometry (for line/element count)", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Area Mesh", "Mesh", "Mesh from lines (for area metric). More accurate than hull.", GH_ParamAccess.tree);
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

            List<int> genList = new List<int>(), indList = new List<int>();
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            double xSpacing = 30, ySpacing = 10;
            Point3d insertPt = Point3d.Origin;
            DA.GetData(3, ref xSpacing);
            DA.GetData(4, ref ySpacing);
            DA.GetData(5, ref insertPt);

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

                    Vector3d offset = new Vector3d(
                        insertPt.X + col * xSpacing,
                        insertPt.Y - row * ySpacing,
                        insertPt.Z);

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
                                nodeTree.Append(new GH_Point(node.Pt + offset), path);
                        }
                    }

                    foreach (var elem in shape.Elems)
                    {
                        if (elem is SG_Elem1D e1d && e1d.Ln.IsValid)
                        {
                            var ln = new Line(e1d.Ln.From + offset, e1d.Ln.To + offset);
                            lineTree.Append(new GH_Line(ln), path);
                        }
                    }

                    var (area, mesh) = ShapeMetrics.MeshAreaFromLinesWithMesh(shape);
                    if (mesh != null && mesh.IsValid)
                    {
                        mesh.Translate(offset);
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
