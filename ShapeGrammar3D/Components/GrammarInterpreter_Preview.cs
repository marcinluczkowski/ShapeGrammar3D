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
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class GrammarInterpreter_Preview : GH_Component
    {
        public GrammarInterpreter_Preview()
          : base("GI_Preview", "GI_Preview",
              "Preview GA population structures as lines and points in a grid layout with cluster coloring",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Shapes", "Shapes",
                "SG_Shape data tree {generation}(individual) from Auto2/Auto4/JsonReader", GH_ParamAccess.tree);    // 0
            pManager.AddNumberParameter("Fitness", "Fitness",
                "Fitness data tree {generation}(individual) matching Shapes tree", GH_ParamAccess.tree);            // 1
            pManager.AddIntegerParameter("ClustGrp", "Clust",
                "Cluster group data tree {generation}(individual) for coloring", GH_ParamAccess.tree);              // 2
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display. One item = one column. " +
                "Multiple items = one column per generation. (-1 = all generations)",
                GH_ParamAccess.list);                                                                                // 3
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual index to display (-1 = all individuals)", GH_ParamAccess.item, -1);                     // 4
            pManager.AddNumberParameter("Top %", "Top%",
                "Show only the top percentage of best structures per generation (0.0–1.0). 1.0 = show all.",
                GH_ParamAccess.item, 1.0);                                                                          // 5
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between generation columns. Default (30, 0, 0) — columns spread along X.",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);                                    // 6
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between individuals in a column. Default (0, 0, -30) — rows go down along Z.",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingWide);                                   // 7
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each cell's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);                                                                                  // 8

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Lines",
                "Element lines {generation column; individual row}(element)", GH_ParamAccess.tree);      // 0
            pManager.AddPointParameter("Points", "Points",
                "Node points {generation column; individual row}(node)", GH_ParamAccess.tree);           // 1
            pManager.AddColourParameter("Colours", "Colours",
                "Cluster colours {generation column; individual row}(per element) – " +
                "matches Lines tree for direct Custom Preview use", GH_ParamAccess.tree);                // 2
            pManager.AddTextParameter("Info", "Info",
                "Preview summary", GH_ParamAccess.item);                                                 // 3
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Inputs ---
            GH_Structure<IGH_Goo> shapesTree = new GH_Structure<IGH_Goo>();
            if (!DA.GetDataTree(0, out shapesTree)) return;

            GH_Structure<GH_Number> fitnessTree = new GH_Structure<GH_Number>();
            DA.GetDataTree(1, out fitnessTree);
            bool hasFitness = fitnessTree != null && fitnessTree.DataCount > 0;

            GH_Structure<GH_Integer> clustTree = new GH_Structure<GH_Integer>();
            DA.GetDataTree(2, out clustTree);
            bool hasCluster = clustTree != null && clustTree.DataCount > 0;

            List<int> generationList = new List<int>();
            DA.GetDataList(3, generationList);

            int individual = -1;
            double topPercent = 1.0;
            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingWide;

            DA.GetData(4, ref individual);
            DA.GetData(5, ref topPercent);
            DA.GetData(6, ref colSpacing);
            DA.GetData(7, ref rowSpacing);
            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 8);

            if (topPercent < 0.0) topPercent = 0.0;
            if (topPercent > 1.0) topPercent = 1.0;

            // --- Build a map of available generations from the shapes tree ---
            // Key = generation index from path, Value = branch index in the tree
            Dictionary<int, int> genToBranch = new Dictionary<int, int>();
            for (int b = 0; b < shapesTree.PathCount; b++)
            {
                int genIndex = shapesTree.Paths[b][0];
                genToBranch[genIndex] = b;
            }

            // --- Resolve generation list ---
            // Default: generation 0 if nothing connected
            if (generationList.Count == 0)
            {
                generationList.Add(0);
            }

            // -1 means all available generations, sorted
            if (generationList.Contains(-1))
            {
                generationList = genToBranch.Keys.OrderBy(k => k).ToList();
            }

            // --- Determine total number of clusters across all data for color mapping ---
            int maxClusterIndex = 0;
            if (hasCluster)
            {
                foreach (GH_Integer ghInt in clustTree.AllData(false))
                {
                    if (ghInt != null && ghInt.Value > maxClusterIndex)
                        maxClusterIndex = ghInt.Value;
                }
            }
            int totalClusters = maxClusterIndex + 1;

            // --- Collect shapes per column (generation) with fitness + cluster ---
            List<List<IndividualEntry>> columns = new List<List<IndividualEntry>>();

            foreach (int genIdx in generationList)
            {
                List<IndividualEntry> column = new List<IndividualEntry>();

                if (!genToBranch.TryGetValue(genIdx, out int branchIdx))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Format("Generation {0} not found in shapes tree.", genIdx));
                    columns.Add(column);
                    continue;
                }

                System.Collections.IList shapeBranch = shapesTree.Branches[branchIdx];

                // Try to find matching fitness branch by path {genIdx}
                GH_Path genPath = new GH_Path(genIdx);
                List<GH_Number> fitBranch = null;
                List<GH_Integer> clustBranch = null;

                if (hasFitness && fitnessTree.PathExists(genPath))
                {
                    fitBranch = (List<GH_Number>)fitnessTree.get_Branch(genPath);
                }

                if (hasCluster && clustTree.PathExists(genPath))
                {
                    clustBranch = (List<GH_Integer>)clustTree.get_Branch(genPath);
                }

                for (int i = 0; i < shapeBranch.Count; i++)
                {
                    // Filter by individual
                    if (individual >= 0 && i != individual)
                        continue;

                    SG_Shape shape = ExtractShape(shapeBranch[i] as IGH_Goo);
                    if (shape == null)
                        continue;

                    double fitness = double.MaxValue;
                    if (fitBranch != null && i < fitBranch.Count && fitBranch[i] != null)
                        fitness = fitBranch[i].Value;

                    int clustGrp = 0;
                    if (clustBranch != null && i < clustBranch.Count && clustBranch[i] != null)
                        clustGrp = clustBranch[i].Value;

                    column.Add(new IndividualEntry
                    {
                        Shape = shape,
                        Fitness = fitness,
                        ClustGrp = clustGrp,
                        GenIndex = genIdx,
                        IndIndex = i
                    });
                }

                // --- Apply top % filter per generation ---
                if (hasFitness && topPercent < 1.0 && column.Count > 0)
                {
                    // Sort ascending (best = lowest fitness for minimization)
                    column.Sort((a, b) => a.Fitness.CompareTo(b.Fitness));

                    int keepCount = Math.Max(1, (int)Math.Ceiling(column.Count * topPercent));
                    if (keepCount < column.Count)
                    {
                        column = column.GetRange(0, keepCount);
                    }
                }

                columns.Add(column);
            }

            int totalShapes = columns.Sum(c => c.Count);
            if (totalShapes == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No shapes found for the specified generation/individual/top%.");
                DA.SetData(3, "No shapes to display.");
                return;
            }

            // --- Build output geometry ---
            // Layout: column = generation (X direction), row = individual (Y direction)
            GH_Structure<GH_Line> linesTree = new GH_Structure<GH_Line>();
            GH_Structure<GH_Point> pointsTree = new GH_Structure<GH_Point>();
            GH_Structure<GH_Colour> coloursTree = new GH_Structure<GH_Colour>();

            int maxRows = 0;

            for (int col = 0; col < columns.Count; col++)
            {
                List<IndividualEntry> column = columns[col];
                if (column.Count > maxRows)
                    maxRows = column.Count;

                for (int row = 0; row < column.Count; row++)
                {
                    IndividualEntry entry = column[row];
                    SG_Shape shape = entry.Shape;
                    Vector3d offset = col * colSpacing + row * rowSpacing;
                    Point3d cellOrigin = Point3d.Origin + offset;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform3D(displayPlane, cellOrigin);

                    GH_Path outPath = new GH_Path(col, row);

                    // Determine cluster colour
                    Color clusterColour = GetClusterColour(entry.ClustGrp, totalClusters);

                    // Extract lines from elements + assign matching colour per line
                    if (shape.Elems != null)
                    {
                        foreach (SG_Element elem in shape.Elems)
                        {
                            if (elem is SG_Elem1D elem1d)
                            {
                                Line ln = elem1d.Ln;
                                Line translated = new Line(ln.From + offset, ln.To + offset);
                                translated.Transform(cellXf);
                                linesTree.Append(new GH_Line(translated), outPath);
                                coloursTree.Append(new GH_Colour(clusterColour), outPath);
                            }
                        }
                    }

                    // Extract points from nodes
                    if (shape.Nodes != null)
                    {
                        foreach (SG_Node node in shape.Nodes)
                        {
                            if (node != null)
                            {
                                Point3d pt = node.Pt + offset;
                                pt.Transform(cellXf);
                                pointsTree.Append(new GH_Point(pt), outPath);
                            }
                        }
                    }
                }
            }

            // --- Info summary ---
            string genLabel = generationList.Count == 1
                ? string.Format("Gen {0}", generationList[0])
                : string.Format("Gens [{0}]", string.Join(", ", generationList));
            string indLabel = individual >= 0
                ? string.Format("Ind {0}", individual)
                : "All individuals";
            string topLabel = topPercent < 1.0
                ? string.Format("Top {0}%", topPercent * 100)
                : "All";

            string info = string.Format(
                "Displaying: {0} structures\n" +
                "Selection: {1}, {2}, {3}\n" +
                "Clusters: {4} (blue → green)\n" +
                "Layout: {5} columns x {6} max rows\n" +
                "Spacing: Col={7}, Row={8}",
                totalShapes,
                genLabel, indLabel, topLabel,
                totalClusters,
                columns.Count, maxRows,
                colSpacing, rowSpacing);

            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, pointsTree);
            DA.SetDataTree(2, coloursTree);
            DA.SetData(3, info);
        }

        /// <summary>
        /// Returns a colour for a given cluster index.
        /// Gradient goes from blue (cluster 0) to green (last cluster) through cyan.
        /// Supports up to 20+ clusters.
        /// </summary>
        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1)
                return Color.FromArgb(0, 150, 255); // default blue

            double t = (double)cluster / (totalClusters - 1);
            t = Math.Clamp(t, 0.0, 1.0);

            // Blue (0,0,255) → Cyan (0,255,255) → Green (0,255,0)
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

            return Color.FromArgb(r,     g, b);
        }

        /// <summary>
        /// Lightweight struct to carry individual data through filtering and layout.
        /// </summary>
        private struct IndividualEntry
        {
            public SG_Shape Shape;
            public double Fitness;
            public int ClustGrp;
            public int GenIndex;
            public int IndIndex;
        }

        /// <summary>
        /// Extracts an SG_Shape from a GH_Goo wrapper.
        /// Handles GH_ObjectWrapper and direct SG_Shape references.
        /// </summary>
        private static SG_Shape ExtractShape(IGH_Goo goo)
        {
            if (goo == null)
                return null;

            // Direct wrapper (from Auto2/Auto4/JsonReader output)
            if (goo is GH_ObjectWrapper wrapper)
                return wrapper.Value as SG_Shape;

            // Try script variable cast
            SG_Shape shape = null;
            goo.CastTo(out shape);
            return shape;
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_CAT_DataPreview; }
        }
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
        {
            get { return new Guid("D6F8A1B3-4C5E-4F7A-B1C3-AE9D8F7A6B5C"); }
        }
    }
}