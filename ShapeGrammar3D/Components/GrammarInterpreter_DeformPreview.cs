using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GrammarInterpreter_DeformPreview : GH_Component
    {
        public GrammarInterpreter_DeformPreview()
          : base("GI_DeformPreview", "GI_DeformPreview",
              "Preview deformed structures from FEM results (TB_Model displacements)",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models",
                "TB_Model data tree {generation}(individual) from Auto4", GH_ParamAccess.tree);       // 0
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display (-1 = all)", GH_ParamAccess.list);                     // 1
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices to display (-1 = all)", GH_ParamAccess.list);                     // 2
            pManager.AddNumberParameter("Scale", "Scale",
                "Deformation scale factor (1.0 = true scale)", GH_ParamAccess.item, 1.0);            // 3
            pManager.AddIntegerParameter("Load Case", "LC",
                "Load case index (-1 = last available)", GH_ParamAccess.item, -1);                    // 4
            pManager.AddNumberParameter("X Spacing", "dX",
                "Horizontal spacing between generation columns", GH_ParamAccess.item, 30.0);          // 5
            pManager.AddNumberParameter("Y Spacing", "dY",
                "Vertical spacing between individual rows", GH_ParamAccess.item, 30.0);               // 6

            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Undeformed", "Undef",
                "Undeformed element lines {col;row}(element)", GH_ParamAccess.tree);                  // 0
            pManager.AddLineParameter("Deformed", "Def",
                "Deformed element lines {col;row}(element)", GH_ParamAccess.tree);                    // 1
            pManager.AddNumberParameter("MaxDisp", "MaxD",
                "Maximum nodal displacement magnitude per individual {col;row}", GH_ParamAccess.tree);// 2
            pManager.AddTextParameter("Info", "Info",
                "Preview summary", GH_ParamAccess.item);                                              // 3
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
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

            double scale = 1.0;
            int lcIndex = -1;
            double xSpacing = 30.0;
            double ySpacing = 30.0;

            DA.GetData(3, ref scale);
            DA.GetData(4, ref lcIndex);
            DA.GetData(5, ref xSpacing);
            DA.GetData(6, ref ySpacing);

            var genToBranch = new Dictionary<int, int>();
            for (int b = 0; b < modelsTree.PathCount; b++)
                genToBranch[modelsTree.Paths[b][0]] = b;

            if (generationList.Count == 0)
                generationList.Add(0);
            if (generationList.Contains(-1))
                generationList = genToBranch.Keys.OrderBy(k => k).ToList();

            bool allIndividuals = individualList.Count == 0 || individualList.Contains(-1);
            var indSet = new HashSet<int>(individualList);

            var undefTree = new GH_Structure<GH_Line>();
            var defTree = new GH_Structure<GH_Line>();
            var maxDispTree = new GH_Structure<GH_Number>();

            int totalModels = 0;
            int maxRows = 0;

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
                    if (model.Elem1Ds == null || model.Nodes == null) continue;

                    Vector3d offset = new Vector3d(col * xSpacing, -row * ySpacing, 0);
                    GH_Path outPath = new GH_Path(col, row);

                    int resolvedLC = ResolveLoadCase(model, lcIndex);
                    bool hasDisps = resolvedLC >= 0;

                    double maxDisp = 0;

                    var nodeDeformedPts = new Dictionary<int, Point3d>();
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
                            nodeDeformedPts[node.Id.Value] = defPt;
                        }
                    }

                    foreach (var elem in model.Elem1Ds)
                    {
                        if (elem.Nodes == null || elem.Nodes.Count < 2) continue;
                        var n0 = elem.Nodes[0];
                        var n1 = elem.Nodes[1];
                        if (n0 == null || n1 == null) continue;

                        Line undef = new Line(n0.Pt + offset, n1.Pt + offset);
                        undefTree.Append(new GH_Line(undef), outPath);

                        if (hasDisps && n0.Id != null && n1.Id != null
                            && nodeDeformedPts.ContainsKey(n0.Id.Value)
                            && nodeDeformedPts.ContainsKey(n1.Id.Value))
                        {
                            Line def = new Line(
                                nodeDeformedPts[n0.Id.Value] + offset,
                                nodeDeformedPts[n1.Id.Value] + offset);
                            defTree.Append(new GH_Line(def), outPath);
                        }
                        else
                        {
                            defTree.Append(new GH_Line(undef), outPath);
                        }
                    }

                    maxDispTree.Append(new GH_Number(maxDisp), outPath);
                    row++;
                    totalModels++;
                }

                if (row > maxRows) maxRows = row;
            }

            string info = string.Format(
                "Models: {0}\nGenerations: [{1}]\nScale: {2}\nLC index: {3}\nLayout: {4} cols x {5} rows",
                totalModels,
                string.Join(", ", generationList),
                scale,
                lcIndex == -1 ? "last" : lcIndex.ToString(),
                generationList.Count, maxRows);

            DA.SetDataTree(0, undefTree);
            DA.SetDataTree(1, defTree);
            DA.SetDataTree(2, maxDispTree);
            DA.SetData(3, info);
        }

        private static int ResolveLoadCase(TB_Model model, int requested)
        {
            if (model.Nodes == null || model.Nodes.Count == 0) return -1;

            var firstWithDisps = model.Nodes.FirstOrDefault(n => n.Disps != null && n.Disps.Count > 0);
            if (firstWithDisps == null) return -1;

            int numLC = firstWithDisps.Disps.Count;
            if (numLC == 0) return -1;

            if (requested < 0 || requested >= numLC)
                return numLC - 1;
            return requested;
        }

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("B2C3D4E5-6F7A-8B9C-0D1E-F2A3B4C5D6E7");
    }
}