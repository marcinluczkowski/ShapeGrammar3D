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
    /// Table preview from SG Assembly. Inputs: Assembly, dX, dY. Displays per-individual:
    /// displacement, objectives, metrics, cluster, Pareto rank/crowding.
    /// </summary>
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class GI_TableAssembly : GH_Component
    {
        private struct TableTextLabelA { public Plane TextPlane; public string Text; }
        private List<TableTextLabelA> _labels = new List<TableTextLabelA>();
        private double _textHeight = 0.3;

        public GI_TableAssembly()
          : base("GI_Table (Assembly)", "GI_Table_A",
              "Displays a text-based data table per individual from Assembly: displacement, objectives, metrics, clustering.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        { }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (Hidden || Locked || _labels == null || _labels.Count == 0) return;
            Color textColor = Color.Black;
            foreach (var lbl in _labels)
                args.Display.Draw3dText(lbl.Text, textColor, lbl.TextPlane, _textHeight, "Arial");
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_FromSg", GH_ParamAccess.item);
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between rows. Default (0, 0, -10).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingCompact);
            pManager.AddPointParameter("Insert Point", "InsPt", "Base point for layout", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddIntegerParameter("Generation", "Gen", "Generation indices (-1 = last)", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind", "Individual indices (-1 = all)", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Show only top N best per cluster per generation. 0 = all (default).", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Load Case", "LC", "Load case index (-1 = last)", GH_ParamAccess.item, -1);
            pManager.AddNumberParameter("Text Height", "TxH", "Text height", GH_ParamAccess.item, 0.3);
            pManager.AddNumberParameter("Line Height", "LnH", "Vertical spacing between lines", GH_ParamAccess.item, 0.4);
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each table's text. Defaults to the world XZ plane.",
                GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("TablePts", "TblPt", "Table anchor point per individual {col;row}", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _labels.Clear();

            GH_SGAssembly ghAssembly = null;
            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly required.");
                return;
            }
            var assembly = ghAssembly.Value;

            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingCompact;
            double textH = 0.3, lineH = 0.4;
            Point3d insertPt = Point3d.Origin;
            List<int> genList = new List<int>();
            List<int> indList = new List<int>();
            int lcIndex = -1;

            DA.GetData(1, ref colSpacing);
            DA.GetData(2, ref rowSpacing);
            DA.GetData(3, ref insertPt);
            DA.GetDataList(4, genList);
            DA.GetDataList(5, indList);
            int topN = 0;
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            DA.GetData(6, ref topN);
            DA.GetData(7, ref lcIndex);
            DA.GetData(8, ref textH);
            DA.GetData(9, ref lineH);
            topN = Math.Max(0, topN);

            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 10);

            _textHeight = Math.Max(0.01, textH);
            var metricNames = assembly.MetricNames ?? new List<string>();

            var gens = assembly.Generations ?? new List<AssemblyGeneration>();
            if (gens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                return;
            }

            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var selectedGens = allGens ? gens.Select(g => g.Generation).OrderBy(g => g).ToList() : genList.Where(g => g >= 0 && gens.Any(gn => gn.Generation == g)).Distinct().OrderBy(g => g).ToList();
            if (selectedGens.Count == 0) selectedGens.Add(gens[gens.Count - 1].Generation);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));

            var eliteSet = new HashSet<(int gen, int ind)>();
            if (topN > 0)
            {
                foreach (var gen in gens.Where(g => selectedGens.Contains(g.Generation)))
                {
                    if (gen?.Individuals == null) continue;
                    var candidates = new List<(int i, double fit, int clust)>();
                    for (int i = 0; i < gen.Individuals.Count; i++)
                    {
                        if (!allInds && (indSet != null && !indSet.Contains(i))) continue;
                        var ind = gen.Individuals[i];
                        if (ind == null || ind.Fitness >= double.MaxValue * 0.5) continue;
                        candidates.Add((i, ind.Fitness, ind.ClustGrp));
                    }
                    foreach (var grp in candidates.GroupBy(c => c.clust))
                    {
                        foreach (var x in grp.OrderBy(c => c.fit).Take(topN))
                            eliteSet.Add((gen.Generation, x.i));
                    }
                }
            }

            // Compute disp min/max for normalized Pareto front display
            double dispMin = double.MaxValue, dispMax = double.MinValue;
            foreach (var gen in gens.Where(g => selectedGens.Contains(g.Generation)))
            {
                if (gen?.Individuals == null) continue;
                for (int i = 0; i < gen.Individuals.Count; i++)
                {
                    if (!allInds && (indSet != null && !indSet.Contains(i))) continue;
                    var ind = gen.Individuals[i];
                    if (ind?.Rank != 0) continue;
                    var model = ind.Model;
                    if (model?.Nodes == null) continue;
                    int lc = ResolveLoadCase(model, lcIndex);
                    if (lc < 0) continue;
                    foreach (var node in model.Nodes)
                    {
                        if (node.Id == null || node.Disps == null || lc >= node.Disps.Count) continue;
                        double[] d = node.Disps[lc];
                        double mag = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                        if (mag < dispMin) dispMin = mag;
                        if (mag > dispMax) dispMax = mag;
                    }
                }
            }
            if (dispMin >= dispMax) { dispMin = 0; dispMax = 1; }
            double dispRange = dispMax - dispMin;

            var tablePtsTree = new GH_Structure<GH_Point>();
            int totalTables = 0;

            for (int col = 0; col < selectedGens.Count; col++)
            {
                int genIdx = selectedGens[col];
                var gen = gens.FirstOrDefault(g => g.Generation == genIdx);
                if (gen == null || gen.Individuals == null) continue;

                int row = 0;
                for (int i = 0; i < gen.Individuals.Count; i++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(i))) continue;
                    if (topN > 0 && !eliteSet.Contains((genIdx, i))) continue;

                    var ind = gen.Individuals[i];

                    Point3d anchorLayout = insertPt + col * colSpacing + row * rowSpacing;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform(displayPlane, anchorLayout);

                    GH_Path outPath = new GH_Path(col, row);
                    tablePtsTree.Append(new GH_Point(anchorLayout), outPath);

                    int lineIdx = 0;
                    AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("--- G{0} I{1} ---", genIdx, i));

                    double maxDisp = 0;
                    TB_Model model = ind?.Model;
                    if (model != null && model.Nodes != null)
                    {
                        int resolvedLC = ResolveLoadCase(model, lcIndex);
                        if (resolvedLC >= 0)
                        {
                            foreach (var node in model.Nodes)
                            {
                                if (node.Id == null || node.Disps == null) continue;
                                if (resolvedLC < node.Disps.Count)
                                {
                                    double[] d = node.Disps[resolvedLC];
                                    double mag = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                                    if (mag > maxDisp) maxDisp = mag;
                                }
                            }
                        }
                    }
                    AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("MaxDisp: {0:F4}", maxDisp));

                    if (ind != null)
                    {
                        if (ind.Fitness >= 0 && ind.Fitness < double.MaxValue * 0.5)
                            AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("Fitness: {0:F4}", ind.Fitness));
                        AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("AvgUtil Dev: {0:F4}", ind.ObjUtil));
                        AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("Feasibility: {0:F4}", ind.ObjFeas));

                        var metrics = ind.AllMetrics();
                        for (int m = 0; m < metrics.Count; m++)
                        {
                            string name = m < metricNames.Count ? metricNames[m] : string.Format("M{0}", m);
                            AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("{0}: {1:F4}", name, metrics[m]));
                        }

                        AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("Cluster: {0}", ind.ClustGrp));

                        AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("Pareto Rank: {0}", ind.Rank));
                        if (ind.Rank == 0)
                        {
                            double normDisp = dispRange > 0 ? (maxDisp - dispMin) / dispRange : 0;
                            AddLabel(anchorLayout, lineH, ref lineIdx, cellXf, string.Format("Pareto front (Disp): {0:F4} (norm {1:F3})", maxDisp, normDisp));
                        }
                    }

                    row++;
                    totalTables++;
                }
            }

            DA.SetDataTree(0, tablePtsTree);
            DA.SetData(1, string.Format("Tables: {0}\nGenerations: [{1}]\nText Height: {2}\nLine Height: {3}",
                totalTables, string.Join(", ", selectedGens), _textHeight, lineH));
        }

        private void AddLabel(Point3d anchor, double lineH, ref int lineIdx, Transform dispXf, string text)
        {
            Point3d pos = new Point3d(anchor.X, anchor.Y - lineIdx * lineH, anchor.Z);
            Plane pl = new Plane(pos, Vector3d.XAxis, Vector3d.YAxis);
            pl.Transform(dispXf);
            _labels.Add(new TableTextLabelA
            {
                TextPlane = pl,
                Text = text
            });
            lineIdx++;
        }

        private static int ResolveLoadCase(TB_Model model, int lcIndex)
        {
            if (model == null || model.Nodes == null || model.Nodes.Count == 0) return -1;
            var first = model.Nodes.FirstOrDefault(n => n.Disps != null && n.Disps.Count > 0);
            if (first == null) return -1;
            int numLC = first.Disps.Count;
            if (numLC == 0) return -1;
            if (lcIndex < 0 || lcIndex >= numLC) return numLC - 1;
            return lcIndex;
        }

        protected override Bitmap Icon => Properties.Resources.icons_CAT_DataPreview;
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;

        public override Guid ComponentGuid => new Guid("D4E5F6A7-B8C9-0123-DEF0-123456789012");
    }
}
