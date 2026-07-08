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
    /// Preview GA structures from Assembly: lines, points, cluster coloring. Same as GI_Preview but Assembly-based.
    /// </summary>
    public class GI_PreviewAssembly : GH_Component
    {
        public GI_PreviewAssembly()
          : base("GI_Preview (Assembly)", "GI_Prev_A",
              "Preview GA structures from Assembly as lines and points with cluster coloring. Top N per cluster (0=all).",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Asm",
                "In-memory GA result from GI_FromSg / GI_LargeSg (contains generations of shapes).", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation index to preview (-1 = all generations present in the assembly).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind",
                "Population index within each generation (-1 = all individuals).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Top N per cluster", "TopN",
                "If > 0, keep only the N lowest-fitness individuals per cluster in each generation; 0 = no filtering.", GH_ParamAccess.item, 0);
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between generation columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between individuals when laying out the preview grid. Default (0, 0, -30).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingWide);
            pManager.AddPointParameter("Insert Point", "Pt",
                "Anchor point for the preview grid.",
                GH_ParamAccess.item, Point3d.Origin);
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each cell's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Member lines", "Ln", "Tree of element axis lines. Path {column; row} = layout cell.", GH_ParamAccess.tree);
            pManager.AddPointParameter("Nodes", "Pt", "Tree of node positions for the same layout.", GH_ParamAccess.tree);
            pManager.AddColourParameter("Colours", "Col", "One colour per element line, keyed by GA cluster id.", GH_ParamAccess.tree);
            pManager.AddTextParameter("Summary", "Info", "Human-readable counts: how many structures were expanded.", GH_ParamAccess.item);
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
            int topN = 0;
            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingWide;
            Point3d insertPt = Point3d.Origin;

            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            DA.GetData(3, ref topN);
            DA.GetData(4, ref colSpacing);
            DA.GetData(5, ref rowSpacing);
            DA.GetData(6, ref insertPt);
            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 7);

            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            topN = Math.Max(0, topN);

            var gens = assembly.Generations ?? new List<AssemblyGeneration>();
            if (gens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                return;
            }

            var selectedGens = allGens ? gens.Select(g => g.Generation).OrderBy(g => g).ToList() : genList.Where(g => g >= 0 && gens.Any(gn => gn.Generation == g)).Distinct().OrderBy(g => g).ToList();
            if (selectedGens.Count == 0) selectedGens.Add(gens.Last().Generation);

            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));
            int totalClusters = 1;
            foreach (var g in gens)
                foreach (var ind in g.Individuals ?? new List<AssemblyIndividual>())
                    if (ind.ClustGrp >= totalClusters - 1) totalClusters = ind.ClustGrp + 2;

            // Build columns: each col = one generation, rows = filtered individuals
            var columns = new List<List<AssemblyIndividual>>();
            foreach (var gen in gens.Where(g => selectedGens.Contains(g.Generation)))
            {
                var col = new List<AssemblyIndividual>();
                if (gen.Individuals == null) { columns.Add(col); continue; }

                var candidates = new List<AssemblyIndividual>();
                for (int i = 0; i < gen.Individuals.Count; i++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(i))) continue;
                    var ind = gen.Individuals[i];
                    if (ind?.Shape == null || ind.Fitness >= double.MaxValue * 0.5) continue;
                    candidates.Add(ind);
                }

                if (topN > 0)
                {
                    foreach (var grp in candidates.GroupBy(c => c.ClustGrp))
                    {
                        var top = grp.OrderBy(c => c.Fitness).Take(topN).ToList();
                        col.AddRange(top);
                    }
                }
                else
                    col.AddRange(candidates);

                columns.Add(col);
            }

            int totalShapes = columns.Sum(c => c.Count);
            if (totalShapes == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No shapes for selected Gen/Ind/TopN.");
                DA.SetData(3, "No shapes.");
                return;
            }

            var linesTree = new GH_Structure<GH_Line>();
            var pointsTree = new GH_Structure<GH_Point>();
            var coloursTree = new GH_Structure<GH_Colour>();
            int maxRows = 0;

            for (int col = 0; col < columns.Count; col++)
            {
                var column = columns[col];
                if (column.Count > maxRows) maxRows = column.Count;

                for (int row = 0; row < column.Count; row++)
                {
                    var ind = column[row];
                    var shape = ind.Shape;
                    Color clr = GetClusterColour(ind.ClustGrp, totalClusters);
                    Vector3d offset = col * colSpacing + row * rowSpacing;
                    Point3d cellOrigin = insertPt + offset;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform3D(displayPlane, cellOrigin);
                    GH_Path outPath = new GH_Path(col, row);

                    if (shape.Elems != null)
                    {
                        foreach (var elem in shape.Elems)
                        {
                            if (elem is SG_Elem1D e1d)
                            {
                                Line ln = new Line(e1d.Ln.From + offset, e1d.Ln.To + offset);
                                ln.Transform(cellXf);
                                linesTree.Append(new GH_Line(ln), outPath);
                                coloursTree.Append(new GH_Colour(clr), outPath);
                            }
                        }
                    }
                    if (shape.Nodes != null)
                    {
                        foreach (var node in shape.Nodes)
                        {
                            if (node != null)
                            {
                                Point3d nodePt = node.Pt + offset;
                                nodePt.Transform(cellXf);
                                pointsTree.Append(new GH_Point(nodePt), outPath);
                            }
                        }
                    }
                }
            }

            string topLabel = topN > 0 ? string.Format("Top {0} per cluster", topN) : "All";
            string genLabel = selectedGens.Count == 1 ? string.Format("Gen {0}", selectedGens[0]) : string.Format("Gens [{0}]", string.Join(",", selectedGens));
            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, pointsTree);
            DA.SetDataTree(2, coloursTree);
            DA.SetData(3, string.Format("Preview Assembly: {0} structures. {1}, {2}. Layout: {3}x{4}", totalShapes, genLabel, topLabel, columns.Count, maxRows));
        }

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0, 1);
            int r = 0, g, b;
            if (t <= 0.5) { g = (int)((t / 0.5) * 255); b = 255; }
            else { g = 255; b = (int)((1 - (t - 0.5) / 0.5) * 255); }
            return Color.FromArgb(r, Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        protected override Bitmap Icon => Properties.Resources.icons_CAT_DataPreview;
        public override Guid ComponentGuid => new Guid("E7F8A2B4-5D6E-4F8B-C2D4-BF0E9F8A7C6D");
    }
}
