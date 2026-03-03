using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GrammarInterpreter_JsonPreview4 : GH_Component
    {
        public GrammarInterpreter_JsonPreview4()
          : base("GI_JsonPreview4", "GI_JsonPrev4",
              "Reads a GA4 run JSON file and previews reconstructed structures " +
              "with section meshes, labels, deformation, and cluster colouring",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("JSON Path", "JSON",
                "Path to GA run JSON file produced by GI_Auto4", GH_ParamAccess.item);                     // 0
            pManager.AddGenericParameter("SG_Shape", "SG_Shape",
                "Initial SG Assembly (same as used in the GA run)", GH_ParamAccess.item);                  // 1
            pManager.AddGenericParameter("Automatic Rules", "Autorules",
                "Rules used in the GA run (same order)", GH_ParamAccess.list);                             // 2
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display (-1 = all)", GH_ParamAccess.list);                          // 3
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices to display (-1 = all)", GH_ParamAccess.list);                          // 4
            pManager.AddNumberParameter("Top %", "Top%",
                "Show only the top percentage of best structures per generation (0.0–1.0). 1.0 = all.",
                GH_ParamAccess.item, 1.0);                                                                 // 5
            pManager.AddBooleanParameter("Show Mesh", "Mesh",
                "Generate cross-section extrusion meshes", GH_ParamAccess.item, false);                    // 6
            pManager.AddBooleanParameter("Show Labels", "Labels",
                "Generate cross-section dimension labels", GH_ParamAccess.item, false);                    // 7
            pManager.AddBooleanParameter("Show Deformation", "Deform",
                "Show deformed structure lines (requires CroSec Opt or solved model)",
                GH_ParamAccess.item, false);                                                                // 8
            pManager.AddNumberParameter("Deformation Scale", "DScale",
                "Deformation scale factor (1.0 = true scale)", GH_ParamAccess.item, 100.0);               // 9
            pManager.AddIntegerParameter("Load Case", "LC",
                "Load case index (-1 = last available)", GH_ParamAccess.item, -1);                         // 10
            pManager.AddBooleanParameter("CroSec Opt", "CSOpt",
                "Apply cross-section optimization during reconstruction " +
                "(uses the same algorithm as Auto4 if the run had it enabled)",
                GH_ParamAccess.item, false);                                                                // 11
            pManager.AddNumberParameter("X Spacing", "dX",
                "Horizontal spacing between generation columns", GH_ParamAccess.item, 30.0);               // 12
            pManager.AddNumberParameter("Y Spacing", "dY",
                "Vertical spacing between individual rows", GH_ParamAccess.item, 30.0);                    // 13

            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Lines",
                "Element lines {col;row}(element)", GH_ParamAccess.tree);                                  // 0
            pManager.AddMeshParameter("Meshes", "Meshes",
                "Cross-section extrusion meshes {col;row}(element)", GH_ParamAccess.tree);                 // 1
            pManager.AddTextParameter("Labels", "Labels",
                "Cross-section dimension text {col;row}(element)", GH_ParamAccess.tree);                   // 2
            pManager.AddPointParameter("LabelPts", "LabelPts",
                "Label anchor points (midpoint of each element) {col;row}(element)", GH_ParamAccess.tree); // 3
            pManager.AddLineParameter("Deformed", "Def",
                "Deformed element lines {col;row}(element)", GH_ParamAccess.tree);                         // 4
            pManager.AddNumberParameter("MaxDisp", "MaxD",
                "Maximum displacement per individual {col;row}", GH_ParamAccess.tree);                     // 5
            pManager.AddColourParameter("Colours", "Colours",
                "Cluster colours {col;row}(per element)", GH_ParamAccess.tree);                            // 6
            pManager.AddNumberParameter("Fitness", "Fitness",
                "Fitness values {col;row}", GH_ParamAccess.tree);                                          // 7
            pManager.AddNumberParameter("Topo", "Topo",
                "First topology metric {col;row}", GH_ParamAccess.tree);                                   // 8
            pManager.AddNumberParameter("Shpe", "Shpe",
                "First shape metric {col;row}", GH_ParamAccess.tree);                                      // 9
            pManager.AddGenericParameter("Shapes", "Shapes",
                "Reconstructed SG_Shapes {col;row}", GH_ParamAccess.tree);                                 // 10
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models",
                "Reconstructed TB_Models {col;row}", GH_ParamAccess.tree);                                 // 11
            pManager.AddTextParameter("Info", "Info",
                "Preview summary", GH_ParamAccess.item);                                                   // 12
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string jsonPath = string.Empty;
            SG_Shape iniShape = new SG_Shape();
            List<SG_Rule> rules = new List<SG_Rule>();

            if (!DA.GetData(0, ref jsonPath)) return;
            if (!DA.GetData(1, ref iniShape)) return;
            if (!DA.GetDataList(2, rules)) return;

            List<int> generationList = new List<int>();
            DA.GetDataList(3, generationList);

            List<int> individualList = new List<int>();
            DA.GetDataList(4, individualList);

            double topPercent = 1.0;
            bool showMesh = false;
            bool showLabels = false;
            bool showDeform = false;
            double deformScale = 100.0;
            int loadCase = -1;
            bool useCroSecOpt = false;
            double xSpacing = 30.0;
            double ySpacing = 30.0;

            DA.GetData(5, ref topPercent);
            DA.GetData(6, ref showMesh);
            DA.GetData(7, ref showLabels);
            DA.GetData(8, ref showDeform);
            DA.GetData(9, ref deformScale);
            DA.GetData(10, ref loadCase);
            DA.GetData(11, ref useCroSecOpt);
            DA.GetData(12, ref xSpacing);
            DA.GetData(13, ref ySpacing);

            topPercent = Math.Clamp(topPercent, 0.0, 1.0);

            if (!File.Exists(jsonPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    string.Format("JSON file not found: {0}", jsonPath));
                return;
            }

            GARunStore store;
            try
            {
                store = GARunStore.LoadFromJson(jsonPath);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    string.Format("Failed to load JSON: {0}", ex.Message));
                return;
            }

            if (store.Generations == null || store.Generations.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "JSON contains no generations.");
                DA.SetData(12, "No generations in file.");
                return;
            }

            var genLookup = new Dictionary<int, GenerationRecord>();
            foreach (var genRec in store.Generations)
                genLookup[genRec.Generation] = genRec;

            if (generationList.Count == 0 || generationList.Contains(-1))
                generationList = genLookup.Keys.OrderBy(k => k).ToList();

            bool allIndividuals = individualList.Count == 0 || individualList.Contains(-1);
            var indSet = new HashSet<int>(individualList);

            int maxCluster = 0;
            foreach (var genRec in store.Generations)
            {
                if (genRec.Individuals == null) continue;
                foreach (var rec in genRec.Individuals)
                    if (rec.ClustGrp > maxCluster) maxCluster = rec.ClustGrp;
            }
            int totalClusters = maxCluster + 1;

            var columns = new List<List<EntryData>>();
            int totalFailed = 0;

            foreach (int genIdx in generationList)
            {
                var column = new List<EntryData>();

                if (!genLookup.TryGetValue(genIdx, out GenerationRecord genRecord))
                {
                    columns.Add(column);
                    continue;
                }

                if (genRecord.Individuals == null)
                {
                    columns.Add(column);
                    continue;
                }

                for (int i = 0; i < genRecord.Individuals.Count; i++)
                {
                    if (!allIndividuals && !indSet.Contains(i)) continue;

                    IndividualRecord rec = genRecord.Individuals[i];

                    SG_Shape shape = null;
                    TB_Model model = null;

                    try
                    {
                        SG_Genotype gt = new SG_Genotype(
                            new List<int>(rec.Chromosome),
                            new List<double>(rec.ChromosomeParam));

                        shape = iniShape.DeepCopy();

                        for (int j = 0; j < rules.Count; j++)
                            rules[j].RuleOperation(ref shape, ref gt);

                        shape.RegisterElemsToNodes();

                        model = new TB_Model(shape);
                        SolveLS slv = new SolveLS(ref model);
                        model = slv.Mdl;

                        if (useCroSecOpt)
                            model = OptimizeCrossSections(model);
                    }
                    catch (Exception ex)
                    {
                        totalFailed++;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            string.Format("Gen {0}, Ind {1}: {2}", genIdx, i, ex.Message));
                        continue;
                    }

                    if (shape == null) continue;

                    column.Add(new EntryData
                    {
                        Shape = shape,
                        Model = model,
                        Record = rec,
                        GenIndex = genIdx,
                        IndIndex = i
                    });
                }

                if (topPercent < 1.0 && column.Count > 0)
                {
                    column.Sort((a, b) => a.Record.Fitness.CompareTo(b.Record.Fitness));
                    int keep = Math.Max(1, (int)Math.Ceiling(column.Count * topPercent));
                    if (keep < column.Count) column = column.GetRange(0, keep);
                }

                columns.Add(column);
            }

            int totalShapes = columns.Sum(c => c.Count);
            if (totalShapes == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No shapes reconstructed.");
                DA.SetData(12, "No shapes to display.");
                return;
            }

            var linesTree = new GH_Structure<GH_Line>();
            var meshesTree = new GH_Structure<GH_Mesh>();
            var labelsTree = new GH_Structure<GH_String>();
            var labelPtsTree = new GH_Structure<GH_Point>();
            var deformedTree = new GH_Structure<GH_Line>();
            var maxDispTree = new GH_Structure<GH_Number>();
            var coloursTree = new GH_Structure<GH_Colour>();
            var fitnessTree = new GH_Structure<GH_Number>();
            var topoTree = new GH_Structure<GH_Number>();
            var shpeTree = new GH_Structure<GH_Number>();
            var shapesTree = new GH_Structure<GH_ObjectWrapper>();
            var modelsTree = new GH_Structure<GH_TB_Model>();

            int maxRows = 0;

            for (int col = 0; col < columns.Count; col++)
            {
                var column = columns[col];
                if (column.Count > maxRows) maxRows = column.Count;

                for (int row = 0; row < column.Count; row++)
                {
                    EntryData entry = column[row];
                    Vector3d offset = new Vector3d(col * xSpacing, -row * ySpacing, 0);
                    GH_Path outPath = new GH_Path(col, row);

                    Color clusterColour = GetClusterColour(entry.Record.ClustGrp, totalClusters);
                    fitnessTree.Append(new GH_Number(entry.Record.Fitness), outPath);
                    topoTree.Append(new GH_Number(entry.Record.Topo), outPath);
                    shpeTree.Append(new GH_Number(entry.Record.Shpe), outPath);
                    shapesTree.Append(new GH_ObjectWrapper(entry.Shape), outPath);
                    if (entry.Model != null)
                        modelsTree.Append(new GH_TB_Model(entry.Model), outPath);

                    if (entry.Shape.Elems == null) continue;

                    int elemIdx = 0;
                    foreach (SG_Element elem in entry.Shape.Elems)
                    {
                        if (!(elem is SG_Elem1D elem1d)) continue;

                        Line ln = new Line(elem1d.Ln.From + offset, elem1d.Ln.To + offset);
                        linesTree.Append(new GH_Line(ln), outPath);
                        coloursTree.Append(new GH_Colour(clusterColour), outPath);

                        double secW = 0, secH = 0;
                        if (entry.Model != null && entry.Model.Elem1Ds != null
                            && elemIdx < entry.Model.Elem1Ds.Count)
                        {
                            var tbSec4 = entry.Model.Elem1Ds[elemIdx].Sec;
                            if (tbSec4 is Section_RHS rhs4)
                            { secW = rhs4.W; secH = rhs4.H; }
                            else if (tbSec4 is Section_Rect rect)
                            { secW = rect.B; secH = rect.H; }
                        }
                        else if (elem1d.CrossSection is SH_CrossSection_Rectangle shRect)
                        {
                            secW = shRect.width;
                            secH = shRect.height;
                        }

                        if (showMesh)
                        {
                            Mesh m = ExtrudeRectSection(ln, secW * 0.001, secH * 0.001);
                            if (m != null)
                                meshesTree.Append(new GH_Mesh(m), outPath);
                        }

                        if (showLabels)
                        {
                            Point3d mid = ln.PointAt(0.5);
                            string label = secW > 0 && secH > 0
                                ? string.Format("{0}x{1}", secW, secH)
                                : "N/A";
                            labelsTree.Append(new GH_String(label), outPath);
                            labelPtsTree.Append(new GH_Point(mid), outPath);
                        }

                        elemIdx++;
                    }

                    if (showDeform && entry.Model != null
                        && entry.Model.Nodes != null && entry.Model.Nodes.Count > 0)
                    {
                        int lcIdx = ResolveLoadCase(entry.Model, loadCase);
                        double maxD = 0;

                        var deformedPts = new Dictionary<int, Point3d>();
                        foreach (var node in entry.Model.Nodes)
                        {
                            if (!node.Id.HasValue) continue;
                            Point3d pt = node.Pt + offset;
                            if (node.Disps != null && lcIdx < node.Disps.Count && node.Disps[lcIdx] != null)
                            {
                                double[] d = node.Disps[lcIdx];
                                if (d.Length >= 3)
                                {
                                    Vector3d dv = new Vector3d(d[0], d[1], d[2]) * deformScale;
                                    pt += dv;
                                    double mag = dv.Length / deformScale;
                                    if (mag > maxD) maxD = mag;
                                }
                            }
                            deformedPts[node.Id.Value] = pt;
                        }

                        if (entry.Model.Elem1Ds != null)
                        {
                            foreach (var tbElem in entry.Model.Elem1Ds)
                            {
                                if (tbElem.Nodes == null || tbElem.Nodes.Count < 2) continue;
                                if (!tbElem.Nodes[0].Id.HasValue || !tbElem.Nodes[1].Id.HasValue) continue;
                                int idA = tbElem.Nodes[0].Id.Value;
                                int idB = tbElem.Nodes[1].Id.Value;
                                if (deformedPts.ContainsKey(idA) && deformedPts.ContainsKey(idB))
                                    deformedTree.Append(new GH_Line(new Line(deformedPts[idA], deformedPts[idB])), outPath);
                            }
                        }

                        maxDispTree.Append(new GH_Number(maxD), outPath);
                    }
                }
            }

            string genLabel = generationList.Count == 1
                ? string.Format("Gen {0}", generationList[0])
                : string.Format("Gens [{0}]", string.Join(", ", generationList));
            string indLabel = allIndividuals ? "All individuals" : string.Format("Ind [{0}]", string.Join(", ", individualList));
            string topLabel = topPercent < 1.0 ? string.Format("Top {0:F0}%", topPercent * 100) : "All";

            string topoMetricNames = store.TopoMetricTypes != null && store.TopoMetricTypes.Count > 0
                ? string.Join(", ", store.TopoMetricTypes.Select(t => TopologyMetrics.GetLabel(t)))
                : "N/A";
            string shpeMetricNames = store.ShapeMetricTypes != null && store.ShapeMetricTypes.Count > 0
                ? string.Join(", ", store.ShapeMetricTypes.Select(s => ShapeMetrics.GetLabel(s)))
                : "N/A";

            string info = string.Format(
                "JSON: {0}\n" +
                "Run ID: {1}\n" +
                "Objectives: {2} | SelfWeight: {3} | CroSecOpt: {4}\n" +
                "Topo Metrics: {5}\n" +
                "Shape Metrics: {6}\n" +
                "Displaying: {7} structures (failed: {8})\n" +
                "Selection: {9}, {10}, {11}\n" +
                "Clusters: {12}\n" +
                "Layout: {13} cols x {14} rows\n" +
                "Mesh: {15}, Labels: {16}, Deform: {17} (scale={18})",
                Path.GetFileName(jsonPath),
                store.RunId,
                store.NumObjectives, store.UseSelfWeight, store.UseCroSecOpt,
                topoMetricNames,
                shpeMetricNames,
                totalShapes, totalFailed,
                genLabel, indLabel, topLabel,
                totalClusters,
                columns.Count, maxRows,
                showMesh ? "ON" : "OFF",
                showLabels ? "ON" : "OFF",
                showDeform ? "ON" : "OFF",
                deformScale);

            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, meshesTree);
            DA.SetDataTree(2, labelsTree);
            DA.SetDataTree(3, labelPtsTree);
            DA.SetDataTree(4, deformedTree);
            DA.SetDataTree(5, maxDispTree);
            DA.SetDataTree(6, coloursTree);
            DA.SetDataTree(7, fitnessTree);
            DA.SetDataTree(8, topoTree);
            DA.SetDataTree(9, shpeTree);
            DA.SetDataTree(10, shapesTree);
            DA.SetDataTree(11, modelsTree);
            DA.SetData(12, info);
        }

        private static int ResolveLoadCase(TB_Model model, int requested)
        {
            if (model.Nodes == null || model.Nodes.Count == 0) return 0;
            int maxLC = model.Nodes.Max(n => n.Disps != null ? n.Disps.Count : 0);
            if (maxLC == 0) return 0;
            if (requested < 0 || requested >= maxLC) return maxLC - 1;
            return requested;
        }

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1)
                return Color.FromArgb(0, 150, 255);

            double t = (double)cluster / (totalClusters - 1);
            t = Math.Clamp(t, 0.0, 1.0);

            int r = 0;
            int g, b;

            if (t <= 0.5)
            {
                double s = t / 0.5;
                g = Math.Clamp((int)(s * 255), 0, 255);
                b = 255;
            }
            else
            {
                double s = (t - 0.5) / 0.5;
                g = 255;
                b = Math.Clamp((int)((1.0 - s) * 255), 0, 255);
            }

            return Color.FromArgb(r, g, b);
        }

        private static Mesh ExtrudeRectSection(Line axis, double widthM, double heightM)
        {
            if (widthM <= 0 || heightM <= 0 || axis.Length < 1e-12)
                return null;

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

            Point3d[] corners = new Point3d[8];
            for (int end = 0; end < 2; end++)
            {
                Point3d origin = end == 0 ? axis.From : axis.To;
                corners[end * 4 + 0] = origin - localY * hw - localZ * hh;
                corners[end * 4 + 1] = origin + localY * hw - localZ * hh;
                corners[end * 4 + 2] = origin + localY * hw + localZ * hh;
                corners[end * 4 + 3] = origin - localY * hw + localZ * hh;
            }

            Mesh mesh = new Mesh();
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

        private static TB_Model OptimizeCrossSections(TB_Model solvedModel)
        {
            if (solvedModel == null || solvedModel.Elem1Ds == null || solvedModel.Elem1Ds.Count == 0)
                return solvedModel;

            const int NUM_SIZES = 20;
            const double STEP_MM = 50.0;

            int elemCount = solvedModel.Elem1Ds.Count;
            int[] sectionIdx = new int[elemCount];

            TB_Model currentModel = RebuildModelWithRectSections(solvedModel, sectionIdx, STEP_MM);
            SolveLS slv = new SolveLS(ref currentModel);
            currentModel = slv.Mdl;

            for (int iter = 0; iter < NUM_SIZES; iter++)
            {
                bool anyChanged = false;

                for (int e = 0; e < currentModel.Elem1Ds.Count; e++)
                {
                    if (sectionIdx[e] >= NUM_SIZES - 1) continue;

                    double util = ComputeElementUtilization(currentModel, currentModel.Elem1Ds[e]);
                    if (util > 1.0)
                    {
                        sectionIdx[e]++;
                        anyChanged = true;
                    }
                }

                if (!anyChanged) break;

                currentModel = RebuildModelWithRectSections(solvedModel, sectionIdx, STEP_MM);
                slv = new SolveLS(ref currentModel);
                currentModel = slv.Mdl;
            }
            return currentModel;
        }

        private static TB_Model RebuildModelWithRectSections(TB_Model template, int[] sectionIdx, double stepMm)
        {
            var newElems = new List<TB_Element_1D>();
            for (int i = 0; i < template.Elem1Ds.Count; i++)
            {
                TB_Element_1D orig = template.Elem1Ds[i];
                double dim = (sectionIdx[i] + 1) * stepMm;
                string tag = string.Format("Rect_{0}x{0}", dim);
                Section_Rect sec = new Section_Rect(orig.Sec.Mat, tag, dim, dim);
                newElems.Add(new TB_Element_1D(orig.Line, orig.Tag, sec, orig.Vz, orig.Buckling_Length));
            }
            return new TB_Model(newElems, template.Sups, template.Loads);
        }

        private static double ComputeElementUtilization(TB_Model model, TB_Element_1D elem)
        {
            const double GAMMA_M0 = 1.0;

            double N_Rd = elem.Sec.Area * elem.Sec.Mat.Fy / GAMMA_M0;
            double My_Rd = elem.Sec.Wy * elem.Sec.Mat.Fy / GAMMA_M0 * 1e-3;
            double Mz_Rd = elem.Sec.Wz * elem.Sec.Mat.Fy / GAMMA_M0 * 1e-3;

            if (N_Rd <= 0 || My_Rd <= 0 || Mz_Rd <= 0)
                return double.MaxValue;

            double maxUtil = 0.0;
            if (model.LCs == null) return maxUtil;

            foreach (int lc in model.LCs)
            {
                int lcId = Array.IndexOf(model.LCs, lc);
                double[] F = elem.Calc_Forces(lcId);

                double N_Ed = Math.Max(Math.Abs(F[0]), Math.Abs(F[6]));
                double My_Ed = Math.Max(Math.Abs(F[4]), Math.Abs(F[10]));
                double Mz_Ed = Math.Max(Math.Abs(F[5]), Math.Abs(F[11]));

                double util = N_Ed / N_Rd + My_Ed / My_Rd + Mz_Ed / Mz_Rd;
                if (util > maxUtil) maxUtil = util;
            }
            return maxUtil;
        }

        private struct EntryData
        {
            public SG_Shape Shape;
            public TB_Model Model;
            public IndividualRecord Record;
            public int GenIndex;
            public int IndIndex;
        }

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("D4E5F6A7-8B9C-0D1E-2F3A-4B5C6D7E8F90");
    }
}
