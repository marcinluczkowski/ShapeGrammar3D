using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    public class GI_RadarChart : GH_Component
    {
        public GI_RadarChart()
            : base("Radar Chart", "Radar",
                  "Visualises n-dimensional metric fingerprints as radar / spider charts " +
                  "arranged in a generation × individual grid.",
                  "ShapeGrammar", "Visualisation")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("All Metrics", "AllMet",
                "Metric values tree {generation;individual}(metric) from Auto4",
                GH_ParamAccess.tree);                                                   // 0
            pManager.AddTextParameter("Metric Names", "MetNm",
                "Ordered metric axis labels from Auto4",
                GH_ParamAccess.list);                                                   // 1
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation indices to display (-1 = last)", GH_ParamAccess.list);      // 2
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual indices to display (-1 = all)", GH_ParamAccess.list);       // 3
            pManager.AddNumberParameter("X Spacing", "dX",
                "Horizontal spacing between columns", GH_ParamAccess.item, 5.0);       // 4
            pManager.AddNumberParameter("Y Spacing", "dY",
                "Vertical spacing between rows", GH_ParamAccess.item, 5.0);            // 5
            pManager.AddNumberParameter("Radius", "R",
                "Maximum axis length (model units)", GH_ParamAccess.item, 1.0);        // 6
            pManager.AddIntervalParameter("Metric Domains", "MDom",
                "Expected [min, max] domain per metric axis for normalization.\n" +
                "Same list as supplied to Auto4's MDom input.\n" +
                "If not supplied, each axis is normalized by observed max.",
                GH_ParamAccess.list);                                                   // 7

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Axes", "Axes",
                "Axis lines per chart {col;row}(axis)", GH_ParamAccess.tree);           // 0
            pManager.AddCurveParameter("Polygon", "Poly",
                "Data polygon per chart {col;row}", GH_ParamAccess.tree);               // 1
            pManager.AddPointParameter("DataPts", "DPts",
                "Data points on axes {col;row}(axis)", GH_ParamAccess.tree);            // 2
            pManager.AddPointParameter("LabelPts", "LPts",
                "Label anchor points {col;row}(axis)", GH_ParamAccess.tree);            // 3
            pManager.AddTextParameter("LabelTxt", "LTxt",
                "Label texts {col;row}(axis)", GH_ParamAccess.tree);                    // 4
            pManager.AddTextParameter("Info", "Info",
                "Summary", GH_ParamAccess.item);                                        // 5
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!DA.GetDataTree(0, out GH_Structure<GH_Number> metricsTree)) return;

            var metricNames = new List<string>();
            if (!DA.GetDataList(1, metricNames) || metricNames.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No metric names provided.");
                return;
            }

            var genList = new List<int>();
            var indList = new List<int>();
            DA.GetDataList(2, genList);
            DA.GetDataList(3, indList);

            double xSpacing = 5.0, ySpacing = 5.0, radius = 1.0;
            DA.GetData(4, ref xSpacing);
            DA.GetData(5, ref ySpacing);
            DA.GetData(6, ref radius);
            if (radius <= 0) radius = 1.0;

            var rawDomains = new List<GH_Interval>();
            DA.GetDataList(7, rawDomains);
            List<Interval> domains = null;
            if (rawDomains.Count > 0)
                domains = rawDomains.Select(d => d.Value).ToList();

            int numAxes = metricNames.Count;
            if (numAxes < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Need at least 2 metrics for a radar chart.");
                return;
            }

            bool hasDomains = domains != null && domains.Count >= numAxes;

            // --- Parse tree structure: paths are {gen;ind} ---
            var allPaths = metricsTree.Paths;
            if (allPaths.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Metrics tree is empty.");
                return;
            }

            var dataByGenInd = new SortedDictionary<int, SortedDictionary<int, double[]>>();
            foreach (GH_Path p in allPaths)
            {
                if (p.Length < 2) continue;
                int gen = p.Indices[0];
                int ind = p.Indices[1];

                var branch = metricsTree.get_Branch(p);
                double[] vals = new double[numAxes];
                for (int m = 0; m < Math.Min(numAxes, branch.Count); m++)
                {
                    if (branch[m] is GH_Number ghNum)
                        vals[m] = ghNum.Value;
                }

                if (!dataByGenInd.ContainsKey(gen))
                    dataByGenInd[gen] = new SortedDictionary<int, double[]>();
                dataByGenInd[gen][ind] = vals;
            }

            if (dataByGenInd.Count == 0) return;

            // --- Filter generations ---
            bool allGens = genList.Count == 0 || (genList.Count == 1 && genList[0] == -1);
            List<int> selectedGens;
            if (allGens)
            {
                int lastGen = dataByGenInd.Keys.Max();
                selectedGens = new List<int> { lastGen };
            }
            else
            {
                selectedGens = genList.Where(g => dataByGenInd.ContainsKey(g)).ToList();
            }
            if (selectedGens.Count == 0) return;

            bool allInds = indList.Count == 0 || (indList.Count == 1 && indList[0] == -1);
            var indSet = new HashSet<int>(indList);

            // --- Compute per-axis normalizer ---
            double[] axisMin = new double[numAxes];
            double[] axisRange = new double[numAxes];

            if (hasDomains)
            {
                for (int m = 0; m < numAxes; m++)
                {
                    axisMin[m] = domains[m].Min;
                    axisRange[m] = domains[m].Max - domains[m].Min;
                    if (axisRange[m] <= 0) axisRange[m] = 1.0;
                }
            }
            else
            {
                double[] axisMax = new double[numAxes];
                for (int m = 0; m < numAxes; m++) axisMax[m] = 1e-15;

                foreach (int gen in selectedGens)
                {
                    foreach (var kvp in dataByGenInd[gen])
                    {
                        if (!allInds && !indSet.Contains(kvp.Key)) continue;
                        for (int m = 0; m < numAxes; m++)
                        {
                            double abs = Math.Abs(kvp.Value[m]);
                            if (abs > axisMax[m]) axisMax[m] = abs;
                        }
                    }
                }
                for (int m = 0; m < numAxes; m++)
                {
                    axisMin[m] = 0;
                    axisRange[m] = axisMax[m];
                }
            }

            // --- Pre-compute axis unit directions ---
            Vector3d[] axisDirs = new Vector3d[numAxes];
            for (int m = 0; m < numAxes; m++)
            {
                double angle = 2.0 * Math.PI * m / numAxes - Math.PI / 2.0;
                axisDirs[m] = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
            }

            // --- Build output ---
            var axesTree = new GH_Structure<GH_Line>();
            var polyTree = new GH_Structure<GH_Curve>();
            var dataPtsTree = new GH_Structure<GH_Point>();
            var labelPtsTree = new GH_Structure<GH_Point>();
            var labelTxtTree = new GH_Structure<GH_String>();

            int col = 0;
            int totalCharts = 0;

            foreach (int gen in selectedGens)
            {
                var genData = dataByGenInd[gen];
                int row = 0;

                foreach (var kvp in genData)
                {
                    if (!allInds && !indSet.Contains(kvp.Key)) continue;

                    double[] vals = kvp.Value;
                    Point3d center = new Point3d(col * xSpacing, -row * ySpacing, 0);
                    GH_Path outPath = new GH_Path(col, row);

                    var polygonPts = new List<Point3d>();

                    for (int m = 0; m < numAxes; m++)
                    {
                        Point3d axisEnd = center + axisDirs[m] * radius;
                        axesTree.Append(new GH_Line(new Line(center, axisEnd)), outPath);

                        double norm = axisRange[m] > 0
                            ? (vals[m] - axisMin[m]) / axisRange[m]
                            : 0;
                        double clampedNorm = Math.Max(0, Math.Min(1, norm));
                        Point3d dataPt = center + axisDirs[m] * (radius * clampedNorm);
                        polygonPts.Add(dataPt);
                        dataPtsTree.Append(new GH_Point(dataPt), outPath);

                        Point3d labelPt = center + axisDirs[m] * (radius * 1.12);
                        labelPtsTree.Append(new GH_Point(labelPt), outPath);

                        string label = hasDomains
                            ? string.Format("{0}\n[{1:G4}–{2:G4}]", metricNames[m], domains[m].Min, domains[m].Max)
                            : metricNames[m];
                        labelTxtTree.Append(new GH_String(label), outPath);
                    }

                    if (polygonPts.Count > 0)
                    {
                        polygonPts.Add(polygonPts[0]);
                        Polyline pl = new Polyline(polygonPts);
                        polyTree.Append(new GH_Curve(pl.ToNurbsCurve()), outPath);
                    }

                    row++;
                    totalCharts++;
                }
                col++;
            }

            DA.SetDataTree(0, axesTree);
            DA.SetDataTree(1, polyTree);
            DA.SetDataTree(2, dataPtsTree);
            DA.SetDataTree(3, labelPtsTree);
            DA.SetDataTree(4, labelTxtTree);

            string normMode = hasDomains ? "user-defined domains" : "observed-max fallback";
            string info = string.Format(
                "Radar Charts: {0}\nAxes: {1}\nGenerations shown: {2}\n" +
                "Radius: {3}\nNormalization: {4}",
                totalCharts, numAxes,
                string.Join(", ", selectedGens),
                radius, normMode);
            DA.SetData(5, info);
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid
            => new Guid("F6A7B8C9-0D1E-2F3A-4B5C-6D7E8F9A0B12");
    }
}
