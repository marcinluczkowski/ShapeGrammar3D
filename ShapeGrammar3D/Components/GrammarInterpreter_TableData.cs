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
    public class GI_TableData : GH_Component
    {
        private struct TableTextLabel
        {
            public Point3d Position;
            public string Text;
        }

        private List<TableTextLabel> _labels = new List<TableTextLabel>();
        private double _textHeight = 0.3;

        public GI_TableData()
            : base("GI_TableData", "GI_Table",
                  "Displays a text-based data table per individual: " +
                  "displacement, optimization objectives, and clustering metrics.",
                  UT.CAT, UT.GR_DATA_PREVIEW)
        { }

        public override bool IsPreviewCapable => true;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (Hidden || Locked || _labels == null || _labels.Count == 0) return;

            Color textColor = Color.Black;
            foreach (var lbl in _labels)
            {
                Plane pl = new Plane(lbl.Position, Vector3d.XAxis, Vector3d.YAxis);
                args.Display.Draw3dText(lbl.Text, textColor, pl, _textHeight, "Arial");
            }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models",
                "TB_Model data tree {generation}(individual) from Auto4", GH_ParamAccess.tree);       // 0
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display (-1 = all)", GH_ParamAccess.list);                     // 1
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices to display (-1 = all)", GH_ParamAccess.list);                     // 2
            pManager.AddIntegerParameter("Load Case", "LC",
                "Load case index (-1 = last available)", GH_ParamAccess.item, -1);                    // 3
            pManager.AddNumberParameter("X Spacing", "dX",
                "Horizontal spacing between generation columns", GH_ParamAccess.item, 30.0);          // 4
            pManager.AddNumberParameter("Y Spacing", "dY",
                "Vertical spacing between individual rows", GH_ParamAccess.item, 10.0);               // 5
            pManager.AddPointParameter("Insert Point", "InsPt",
                "Base point for the grid layout", GH_ParamAccess.item, Point3d.Origin);               // 6
            pManager.AddNumberParameter("All Metrics", "AllMet",
                "Metric values tree {generation;individual}(metric) from Auto4",
                GH_ParamAccess.tree);                                                                  // 7
            pManager.AddTextParameter("Metric Names", "MetNm",
                "Ordered metric axis labels from Auto4",
                GH_ParamAccess.list);                                                                  // 8
            pManager.AddIntegerParameter("Cluster Groups", "Clust",
                "Cluster group per individual {generation}(individual) from Auto4",
                GH_ParamAccess.tree);                                                                  // 9
            pManager.AddNumberParameter("Text Height", "TxH",
                "Text height in model units", GH_ParamAccess.item, 0.3);                              // 10
            pManager.AddNumberParameter("Line Height", "LnH",
                "Vertical spacing between text lines (model units)", GH_ParamAccess.item, 0.4);       // 11
            pManager.AddNumberParameter("All Fitness", "AllFit",
                "Fitness values {generation}(individual) from Auto4",
                GH_ParamAccess.tree);                                                                  // 12
            pManager.AddNumberParameter("Obj Avg Util", "ObjUtil",
                "Avg utilization deviation from 90% target {generation}(individual) from Auto4",
                GH_ParamAccess.tree);                                                                  // 13
            pManager.AddNumberParameter("Obj Feasibility", "ObjFeas",
                "Feasibility objective {generation}(individual) from Auto4",
                GH_ParamAccess.tree);                                                                  // 14
            pManager.AddIntegerParameter("Pareto Rank", "Rank",
                "NSGA-II rank per individual {generation}(individual): 0=first front, 1=second, etc. From Auto4. When set, table shows Rank, Crowding (selection), and Pareto front (Disp) for rank-0.",
                GH_ParamAccess.tree);                                                                  // 15
            pManager.AddNumberParameter("Crowding", "Crowd",
                "NSGA-II crowding distance {generation}(individual) from Auto4. With Rank, shows selection order: lower rank then higher crowding.",
                GH_ParamAccess.tree);                                                                  // 16

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
            pManager[13].Optional = true;
            pManager[14].Optional = true;
            pManager[15].Optional = true;
            pManager[16].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("TablePts", "TblPt",
                "Table anchor point per individual {col;row}", GH_ParamAccess.tree);                   // 0
            pManager.AddTextParameter("Info", "Info",
                "Summary", GH_ParamAccess.item);                                                       // 1
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _labels.Clear();

            GH_Structure<GH_TB_Model> modelsTree = new GH_Structure<GH_TB_Model>();
            if (!DA.GetDataTree(0, out modelsTree)) return;
            if (modelsTree == null || modelsTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No models provided.");
                return;
            }

            List<int> generationList = new List<int>();
            DA.GetDataList(1, generationList);

            List<int> individualList = new List<int>();
            DA.GetDataList(2, individualList);

            int lcIndex = -1;
            double xSpacing = 30.0;
            double ySpacing = 10.0;
            Point3d insertPt = Point3d.Origin;
            double textH = 0.3;
            double lineH = 0.4;

            DA.GetData(3, ref lcIndex);
            DA.GetData(4, ref xSpacing);
            DA.GetData(5, ref ySpacing);
            DA.GetData(6, ref insertPt);

            DA.GetDataTree(7, out GH_Structure<GH_Number> metricsTree);

            var metricNames = new List<string>();
            DA.GetDataList(8, metricNames);

            DA.GetDataTree(9, out GH_Structure<GH_Integer> clustTree);

            DA.GetData(10, ref textH);
            DA.GetData(11, ref lineH);
            _textHeight = Math.Max(0.01, textH);

            DA.GetDataTree(12, out GH_Structure<GH_Number> fitnessTree);
            DA.GetDataTree(13, out GH_Structure<GH_Number> objVolTree);
            DA.GetDataTree(14, out GH_Structure<GH_Number> objFeasTree);
            DA.GetDataTree(15, out GH_Structure<GH_Integer> rankTree);
            DA.GetDataTree(16, out GH_Structure<GH_Number> crowdingTree);

            var fitByGenInd = ParseFlatTree(fitnessTree);
            var crowdingByGenInd = ParseFlatTree(crowdingTree);
            var volByGenInd = ParseFlatTree(objVolTree);
            var feasByGenInd = ParseFlatTree(objFeasTree);

            var genToBranch = new Dictionary<int, int>();
            for (int b = 0; b < modelsTree.PathCount; b++)
                genToBranch[modelsTree.Paths[b][0]] = b;

            if (generationList.Count == 0)
                generationList.Add(0);
            if (generationList.Contains(-1))
                generationList = genToBranch.Keys.OrderBy(k => k).ToList();

            bool allIndividuals = individualList.Count == 0 || individualList.Contains(-1);
            var indSet = new HashSet<int>(individualList);

            var clusterByGenInd = new Dictionary<int, Dictionary<int, int>>();
            if (clustTree != null)
            {
                foreach (GH_Path cp in clustTree.Paths)
                {
                    if (cp.Length < 1) continue;
                    int cGen = cp.Indices[0];
                    var cBranch = clustTree.get_Branch(cp);
                    if (!clusterByGenInd.ContainsKey(cGen))
                        clusterByGenInd[cGen] = new Dictionary<int, int>();
                    for (int ci = 0; ci < cBranch.Count; ci++)
                    {
                        if (cBranch[ci] is GH_Integer ghInt)
                            clusterByGenInd[cGen][ci] = ghInt.Value;
                    }
                }
            }

            var metricsByGenInd = new Dictionary<int, Dictionary<int, double[]>>();
            if (metricsTree != null)
            {
                foreach (GH_Path mp in metricsTree.Paths)
                {
                    if (mp.Length < 2) continue;
                    int gen = mp.Indices[0];
                    int ind = mp.Indices[1];
                    var branch = metricsTree.get_Branch(mp);
                    double[] vals = new double[branch.Count];
                    for (int m = 0; m < branch.Count; m++)
                    {
                        if (branch[m] is GH_Number ghNum) vals[m] = ghNum.Value;
                    }
                    if (!metricsByGenInd.ContainsKey(gen))
                        metricsByGenInd[gen] = new Dictionary<int, double[]>();
                    metricsByGenInd[gen][ind] = vals;
                }
            }

            var rankByGenInd = new Dictionary<int, Dictionary<int, int>>();
            if (rankTree != null)
            {
                foreach (GH_Path rp in rankTree.Paths)
                {
                    if (rp.Length < 1) continue;
                    int gen = rp.Indices[0];
                    var branch = rankTree.get_Branch(rp);
                    if (!rankByGenInd.ContainsKey(gen))
                        rankByGenInd[gen] = new Dictionary<int, int>();
                    for (int ri = 0; ri < branch.Count; ri++)
                    {
                        if (branch[ri] is GH_Integer ghInt)
                            rankByGenInd[gen][ri] = ghInt.Value;
                    }
                }
            }

            var tablePtsTree = new GH_Structure<GH_Point>();
            int totalTables = 0;

            for (int col = 0; col < generationList.Count; col++)
            {
                int genIdx = generationList[col];
                if (!genToBranch.TryGetValue(genIdx, out int branchIdx)) continue;

                GH_Path genPath = new GH_Path(genIdx);
                if (!modelsTree.PathExists(genPath)) continue;

                List<GH_TB_Model> modelBranch = (List<GH_TB_Model>)modelsTree.get_Branch(genPath);

                int row = 0;
                for (int i = 0; i < modelBranch.Count; i++)
                {
                    if (!allIndividuals && !indSet.Contains(i)) continue;
                    if (modelBranch[i] == null || modelBranch[i].Value == null) continue;

                    TB_Model model = modelBranch[i].Value;
                    if (model.Nodes == null) continue;

                    Point3d anchor = new Point3d(
                        insertPt.X + col * xSpacing,
                        insertPt.Y - row * ySpacing,
                        insertPt.Z);

                    GH_Path outPath = new GH_Path(col, row);
                    tablePtsTree.Append(new GH_Point(anchor), outPath);

                    int lineIdx = 0;

                    AddLabel(anchor, lineH, ref lineIdx,
                        string.Format("--- G{0} I{1} ---", genIdx, i));

                    int resolvedLC = ResolveLoadCase(model, lcIndex);
                    double maxDisp = 0;
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
                    AddLabel(anchor, lineH, ref lineIdx,
                        string.Format("MaxDisp: {0:F4}", maxDisp));

                    if (fitByGenInd.TryGetValue(genIdx, out var genFit)
                        && genFit.TryGetValue(i, out double fitVal))
                    {
                        AddLabel(anchor, lineH, ref lineIdx,
                            string.Format("Fitness: {0:F4}", fitVal));
                    }

                    if (volByGenInd.TryGetValue(genIdx, out var genVol)
                        && genVol.TryGetValue(i, out double volVal))
                    {
                        AddLabel(anchor, lineH, ref lineIdx,
                            string.Format("AvgUtil Dev: {0:F4}", volVal));
                    }

                    if (feasByGenInd.TryGetValue(genIdx, out var genFeas)
                        && genFeas.TryGetValue(i, out double feasVal))
                    {
                        AddLabel(anchor, lineH, ref lineIdx,
                            string.Format("Feasibility: {0:F4}", feasVal));
                    }

                    if (metricsByGenInd.TryGetValue(genIdx, out var genMetrics)
                        && genMetrics.TryGetValue(i, out double[] metVals))
                    {
                        for (int m = 0; m < metVals.Length; m++)
                        {
                            string name = (m < metricNames.Count) ? metricNames[m] : string.Format("M{0}", m);
                            AddLabel(anchor, lineH, ref lineIdx,
                                string.Format("{0}: {1:F4}", name, metVals[m]));
                        }
                    }

                    if (clusterByGenInd.TryGetValue(genIdx, out var genClust)
                        && genClust.TryGetValue(i, out int clustId))
                    {
                        AddLabel(anchor, lineH, ref lineIdx,
                            string.Format("Cluster: {0}", clustId));
                    }

                    if (rankByGenInd.TryGetValue(genIdx, out var genRank)
                        && genRank.TryGetValue(i, out int rank))
                    {
                        AddLabel(anchor, lineH, ref lineIdx,
                            string.Format("Pareto Rank: {0}", rank));
                        if (crowdingByGenInd.TryGetValue(genIdx, out var genCrowd)
                            && genCrowd.TryGetValue(i, out double crowd))
                            AddLabel(anchor, lineH, ref lineIdx,
                                string.Format("Crowding (selection): {0:F4}", crowd));
                        if (rank == 0)
                            AddLabel(anchor, lineH, ref lineIdx,
                                string.Format("Pareto front (Disp): {0:F4}", maxDisp));
                    }

                    row++;
                    totalTables++;
                }
            }

            DA.SetDataTree(0, tablePtsTree);

            string info = string.Format(
                "Tables: {0}\nGenerations: [{1}]\nText Height: {2}\nLine Height: {3}",
                totalTables,
                string.Join(", ", generationList),
                _textHeight, lineH);
            DA.SetData(1, info);
        }

        private void AddLabel(Point3d anchor, double lineH, ref int lineIdx, string text)
        {
            _labels.Add(new TableTextLabel
            {
                Position = new Point3d(anchor.X, anchor.Y - lineIdx * lineH, anchor.Z),
                Text = text
            });
            lineIdx++;
        }

        private static Dictionary<int, Dictionary<int, double>> ParseFlatTree(GH_Structure<GH_Number> tree)
        {
            var result = new Dictionary<int, Dictionary<int, double>>();
            if (tree == null || tree.DataCount == 0) return result;

            foreach (GH_Path p in tree.Paths)
            {
                if (p.Length < 1) continue;
                int gen = p.Indices[0];
                var branch = tree.get_Branch(p);
                if (!result.ContainsKey(gen))
                    result[gen] = new Dictionary<int, double>();
                for (int idx = 0; idx < branch.Count; idx++)
                {
                    if (branch[idx] is GH_Number ghNum)
                        result[gen][idx] = ghNum.Value;
                }
            }
            return result;
        }

        private static int ResolveLoadCase(TB_Model model, int lcIndex)
        {
            if (model.Nodes == null || model.Nodes.Count == 0) return -1;
            var firstNode = model.Nodes.FirstOrDefault(n => n.Disps != null && n.Disps.Count > 0);
            if (firstNode == null) return -1;
            int numLC = firstNode.Disps.Count;
            if (numLC == 0) return -1;
            if (lcIndex < 0 || lcIndex >= numLC) return numLC - 1;
            return lcIndex;
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid
            => new Guid("A1B2C3D4-E5F6-7890-AB12-CD34EF567890");
    }
}
