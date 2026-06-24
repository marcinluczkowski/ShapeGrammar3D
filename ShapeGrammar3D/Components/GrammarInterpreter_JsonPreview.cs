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
    public class GrammarInterpreter_JsonPreview : GH_Component
    {
        public GrammarInterpreter_JsonPreview()
          : base("GI_JsonPreview", "GI_JsonPrev",
              "Reads a GA run JSON file and previews reconstructed structures " +
              "as lines and points in a grid layout with cluster coloring",
              UT.CAT, UT.GR_INT)
        {
        }

        // ── Inputs ──────────────────────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("JSON Path", "JSON",
                "Path to GA run JSON file produced by GI_Auto3", GH_ParamAccess.item);                   // 0
            pManager.AddGenericParameter("SG_Shape", "SG_Shape",
                "Initial SG Assembly (same as used in the GA run)", GH_ParamAccess.item);                // 1
            pManager.AddGenericParameter("Automatic Rules", "Autorules",
                "Rules used in the GA run (same order as GI_Auto3)", GH_ParamAccess.list);               // 2
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display. (-1 = all generations)",
                GH_ParamAccess.list);                                                                     // 3
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual index to display (-1 = all individuals)", GH_ParamAccess.item, -1);          // 4
            pManager.AddNumberParameter("Top %", "Top%",
                "Show only the top percentage of best structures per generation (0.0–1.0). " +
                "1.0 = show all.", GH_ParamAccess.item, 1.0);                                            // 5
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between generation columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);                      // 6
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between individuals in a column. Default (0, 0, -30).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingWide);                     // 7
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each cell's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);                                                                     // 8

            pManager[3].Optional = true;
            pManager[8].Optional = true;
        }

        // ── Outputs ─────────────────────────────────────────────────────────
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
            pManager.AddTextParameter("ClustInfo", "ClustInfo",
                "Clustering metrics per individual {generation column; individual row}: " +
                "topoRes, shapeRes, fitnessRes", GH_ParamAccess.tree);                                  // 4
        }

        // ── Solve ───────────────────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Inputs ---
            string jsonPath = string.Empty;
            SG_Shape iniShape = new SG_Shape();
            List<SG_Rule> rules = new List<SG_Rule>();

            if (!DA.GetData(0, ref jsonPath)) return;
            if (!DA.GetData(1, ref iniShape)) return;
            if (!DA.GetDataList(2, rules)) return;

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

            // --- Validate file ---
            if (!File.Exists(jsonPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    string.Format("JSON file not found: {0}", jsonPath));
                return;
            }

            // --- Load JSON ---
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
                DA.SetData(3, "No generations in file.");
                return;
            }

            // --- Resolve generation list ---
            // Build lookup: generation index → GenerationRecord
            Dictionary<int, GenerationRecord> genLookup = new Dictionary<int, GenerationRecord>();
            foreach (GenerationRecord genRec in store.Generations)
            {
                genLookup[genRec.Generation] = genRec;
            }

            // Default: all generations if nothing connected
            if (generationList.Count == 0 || generationList.Contains(-1))
            {
                generationList = genLookup.Keys.OrderBy(k => k).ToList();
            }

            // --- Determine total number of clusters across all data for color mapping ---
            int maxClusterIndex = 0;
            foreach (GenerationRecord genRec in store.Generations)
            {
                if (genRec.Individuals == null) continue;
                foreach (IndividualRecord rec in genRec.Individuals)
                {
                    if (rec.ClustGrp > maxClusterIndex)
                        maxClusterIndex = rec.ClustGrp;
                }
            }
            int totalClusters = maxClusterIndex + 1;

            // --- Reconstruct shapes and collect entries per column (generation) ---
            List<List<ReconstructedEntry>> columns = new List<List<ReconstructedEntry>>();
            int totalFailed = 0;

            foreach (int genIdx in generationList)
            {
                List<ReconstructedEntry> column = new List<ReconstructedEntry>();

                if (!genLookup.TryGetValue(genIdx, out GenerationRecord genRecord))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Format("Generation {0} not found in JSON.", genIdx));
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
                    // Filter by individual
                    if (individual >= 0 && i != individual)
                        continue;

                    IndividualRecord rec = genRecord.Individuals[i];

                    // Reconstruct shape from genotype
                    SG_Shape shape = null;
                    try
                    {
                        SG_Genotype gt = new SG_Genotype(
                            new List<int>(rec.Chromosome),
                            new List<double>(rec.ChromosomeParam));

                        shape = iniShape.DeepCopy();

                        for (int j = 0; j < rules.Count; j++)
                        {
                            rules[j].RuleOperation(ref shape, ref gt);
                        }

                        shape.RegisterElemsToNodes();
                    }
                    catch (Exception ex)
                    {
                        totalFailed++;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            string.Format("Gen {0}, Ind {1} reconstruction failed: {2}",
                                genIdx, i, ex.Message));
                        continue;
                    }

                    if (shape == null)
                        continue;

                    column.Add(new ReconstructedEntry
                    {
                        Shape = shape,
                        Fitness = rec.Fitness,
                        Topo = rec.Topo,
                        Shpe = rec.Shpe,
                        ClustGrp = rec.ClustGrp,
                        GenIndex = genIdx,
                        IndIndex = i
                    });
                }

                // --- Apply top % filter per generation ---
                if (topPercent < 1.0 && column.Count > 0)
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
                    "No shapes reconstructed for the specified generation/individual/top%.");
                DA.SetData(3, "No shapes to display.");
                return;
            }

            // --- Build output geometry ---
            // Layout: column = generation (X direction), row = individual (Y direction)
            GH_Structure<GH_Line> linesTree = new GH_Structure<GH_Line>();
            GH_Structure<GH_Point> pointsTree = new GH_Structure<GH_Point>();
            GH_Structure<GH_Colour> coloursTree = new GH_Structure<GH_Colour>();
            GH_Structure<GH_String> clustInfoTree = new GH_Structure<GH_String>();

            int maxRows = 0;

            for (int col = 0; col < columns.Count; col++)
            {
                List<ReconstructedEntry> column = columns[col];
                if (column.Count > maxRows)
                    maxRows = column.Count;

                for (int row = 0; row < column.Count; row++)
                {
                    ReconstructedEntry entry = column[row];
                    SG_Shape shape = entry.Shape;
                    Vector3d offset = col * colSpacing + row * rowSpacing;
                    Point3d cellOrigin = Point3d.Origin + offset;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform3D(displayPlane, cellOrigin);

                    GH_Path outPath = new GH_Path(col, row);

                    // Determine cluster colour
                    Color clusterColour = GetClusterColour(entry.ClustGrp, totalClusters);

                    // Clustering metrics string
                    string clustInfo = string.Format(
                        "topoRes: {0:F4} ; shapeRes: {1:F4} ; fitnessRes: {2:F6}",
                        entry.Topo, entry.Shpe, entry.Fitness);
                    clustInfoTree.Append(new GH_String(clustInfo), outPath);

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

                    // --- Clustering info: topoRes, shapeRes, fitnessRes ---
                    // Note: for existing clusters only (ClustGrp >= 0)
                    if (entry.ClustGrp >= 0)
                    {
                        string topoRes = entry.Topo.ToString("F3");
                        string shapeRes = entry.Shpe.ToString("F3");
                        string fitnessRes = entry.Fitness.ToString("F3");

                        clustInfoTree.Append(new GH_String(topoRes), outPath);
                        clustInfoTree.Append(new GH_String(shapeRes), outPath);
                        clustInfoTree.Append(new GH_String(fitnessRes), outPath);
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
                "JSON: {0}\n" +
                "Run ID: {1}\n" +
                "Displaying: {2} structures (failed: {3})\n" +
                "Selection: {4}, {5}, {6}\n" +
                "Clusters: {7} (blue → green)\n" +
                "Layout: {8} columns x {9} max rows\n" +
                "Spacing: Col={10}, Row={11}",
                Path.GetFileName(jsonPath),
                store.RunId,
                totalShapes, totalFailed,
                genLabel, indLabel, topLabel,
                totalClusters,
                columns.Count, maxRows,
                colSpacing, rowSpacing);

            DA.SetDataTree(0, linesTree);
            DA.SetDataTree(1, pointsTree);
            DA.SetDataTree(2, coloursTree);
            DA.SetData(3, info);
            DA.SetDataTree(4, clustInfoTree);
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

            return Color.FromArgb(r, g, b);
        }

        /// <summary>
        /// Lightweight struct to carry reconstructed individual data through filtering and layout.
        /// </summary>
        private struct ReconstructedEntry
        {
            public SG_Shape Shape;
            public double Fitness;
            public double Topo;
            public double Shpe;
            public int ClustGrp;
            public int GenIndex;
            public int IndIndex;
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_Generic; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("A3B7C1D5-6E8F-4A2B-9D1E-F0C3B5A7D9E1"); }
        }
    }
}