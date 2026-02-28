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
    public class GrammarInterpreter_ShapePreview : GH_Component
    {
        public GrammarInterpreter_ShapePreview()
          : base("GI_ShapePreview", "GI_ShapePreview",
              "Preview GA structures with element lines, cross-section meshes, and dimension labels",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Shapes", "Shapes",
                "SG_Shape data tree {generation}(individual) from Auto4", GH_ParamAccess.tree);       // 0
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models",
                "TB_Model data tree {generation}(individual) from Auto4", GH_ParamAccess.tree);       // 1
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display (-1 = all)", GH_ParamAccess.list);                     // 2
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices to display (-1 = all)", GH_ParamAccess.list);                     // 3
            pManager.AddBooleanParameter("Show Mesh", "Mesh",
                "Generate cross-section extrusion meshes", GH_ParamAccess.item, false);               // 4
            pManager.AddBooleanParameter("Show Labels", "Labels",
                "Generate cross-section dimension labels", GH_ParamAccess.item, false);               // 5
            pManager.AddNumberParameter("X Spacing", "dX",
                "Horizontal spacing between generation columns", GH_ParamAccess.item, 30.0);          // 6
            pManager.AddNumberParameter("Y Spacing", "dY",
                "Vertical spacing between individual rows", GH_ParamAccess.item, 30.0);               // 7

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Lines",
                "Element lines {col;row}(element)", GH_ParamAccess.tree);                             // 0
            pManager.AddMeshParameter("Meshes", "Meshes",
                "Cross-section extrusion meshes {col;row}(element)", GH_ParamAccess.tree);            // 1
            pManager.AddTextParameter("Labels", "Labels",
                "Cross-section dimension text {col;row}(element)", GH_ParamAccess.tree);              // 2
            pManager.AddPointParameter("LabelPts", "LabelPts",
                "Label anchor points (midpoint of each element) {col;row}(element)", GH_ParamAccess.tree); // 3
            pManager.AddTextParameter("Info", "Info",
                "Preview summary", GH_ParamAccess.item);                                              // 4
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Structure<IGH_Goo> shapesTree = new GH_Structure<IGH_Goo>();
            if (!DA.GetDataTree(0, out shapesTree)) return;

            GH_Structure<GH_TB_Model> modelsTree = new GH_Structure<GH_TB_Model>();
            DA.GetDataTree(1, out modelsTree);
            bool hasModels = modelsTree != null && modelsTree.DataCount > 0;

            List<int> generationList = new List<int>();
            DA.GetDataList(2, generationList);

            List<int> individualList = new List<int>();
            DA.GetDataList(3, individualList);

            bool showMesh = false;
            bool showLabels = false;
            double xSpacing = 30.0;
            double ySpacing = 30.0;

            DA.GetData(4, ref showMesh);
            DA.GetData(5, ref showLabels);
            DA.GetData(6, ref xSpacing);
            DA.GetData(7, ref ySpacing);

            var genToBranch = new Dictionary<int, int>();
            for (int b = 0; b < shapesTree.PathCount; b++)
                genToBranch[shapesTree.Paths[b][0]] = b;

            if (generationList.Count == 0)
                generationList.Add(0);
            if (generationList.Contains(-1))
                generationList = genToBranch.Keys.OrderBy(k => k).ToList();

            bool allIndividuals = individualList.Count == 0 || individualList.Contains(-1);
            var indSet = new HashSet<int>(individualList);

            var linesTree = new GH_Structure<GH_Line>();
            var meshesTree = new GH_Structure<GH_Mesh>();
            var labelsTree = new GH_Structure<GH_String>();
            var labelPtsTree = new GH_Structure<GH_Point>();

            int totalShapes = 0;
            int maxRows = 0;

            for (int col = 0; col < generationList.Count; col++)
            {
                int genIdx = generationList[col];
                if (!genToBranch.TryGetValue(genIdx, out int branchIdx)) continue;

                System.Collections.IList shapeBranch = shapesTree.Branches[branchIdx];
                GH_Path genPath = new GH_Path(genIdx);

                List<GH_TB_Model> modelBranch = null;
                if (hasModels && modelsTree.PathExists(genPath))
                    modelBranch = (List<GH_TB_Model>)modelsTree.get_Branch(genPath);

                int row = 0;
                for (int i = 0; i < shapeBranch.Count; i++)
                {
                    if (!allIndividuals && !indSet.Contains(i)) continue;

                    SG_Shape shape = ExtractShape(shapeBranch[i] as IGH_Goo);
                    if (shape == null) continue;

                    TB_Model model = null;
                    if (modelBranch != null && i < modelBranch.Count && modelBranch[i] != null)
                        model = modelBranch[i].Value;

                    Vector3d offset = new Vector3d(col * xSpacing, -row * ySpacing, 0);
                    GH_Path outPath = new GH_Path(col, row);

                    if (shape.Elems != null)
                    {
                        int elemIdx = 0;
                        foreach (SG_Element elem in shape.Elems)
                        {
                            if (!(elem is SG_Elem1D elem1d)) continue;

                            Line ln = new Line(elem1d.Ln.From + offset, elem1d.Ln.To + offset);
                            linesTree.Append(new GH_Line(ln), outPath);

                            double secW = 0, secH = 0;
                            TB_Section tbSec = GetSectionForElement(model, elemIdx);
                            if (tbSec is Section_Rect rect)
                            {
                                secW = rect.B;
                                secH = rect.H;
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
                    }

                    row++;
                    totalShapes++;
                }

                if (row > maxRows) maxRows = row;
            }

            string info = string.Format(
                "Structures: {0}\nGenerations: [{1}]\nLayout: {2} cols x {3} rows\nMesh: {4}, Labels: {5}",
                totalShapes,
                string.Join(", ", generationList),
                generationList.Count, maxRows,
                showMesh ? "ON" : "OFF",
                showLabels ? "ON" : "OFF");

            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, meshesTree);
            DA.SetDataTree(2, labelsTree);
            DA.SetDataTree(3, labelPtsTree);
            DA.SetData(4, info);
        }

        private static TB_Section GetSectionForElement(TB_Model model, int elemIndex)
        {
            if (model == null || model.Elem1Ds == null || elemIndex >= model.Elem1Ds.Count)
                return null;
            return model.Elem1Ds[elemIndex].Sec;
        }

        /// <summary>
        /// Creates a simple rectangular box mesh extruded along a line.
        /// Dimensions in meters (converted from mm by the caller).
        /// </summary>
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

        private static SG_Shape ExtractShape(IGH_Goo goo)
        {
            if (goo == null) return null;
            if (goo is GH_ObjectWrapper wrapper)
                return wrapper.Value as SG_Shape;
            SG_Shape shape = null;
            goo.CastTo(out shape);
            return shape;
        }

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("A1B2C3D4-5E6F-7A8B-9C0D-E1F2A3B4C5D6");
    }
}