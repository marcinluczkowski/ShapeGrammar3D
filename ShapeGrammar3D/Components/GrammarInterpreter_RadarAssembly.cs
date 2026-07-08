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
    /// Radar chart from SG Assembly. Outputs axes, polygons, and bakeable
    /// TextEntity labels (metric names, values, cluster id).
    /// </summary>
    public class GI_RadarAssembly : GH_Component
    {
        public GI_RadarAssembly()
          : base("GI_Radar (Assembly)", "GI_Radar_A",
              "Radar chart from Assembly. Visualises metric fingerprints. Outputs bakeable TextEntity labels (metrics + cluster id).",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly from GI_FromSg", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all (default).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Individual", "Ind", "Which individual(s). -1 = all (default).", GH_ParamAccess.list, -1);
            pManager.AddIntegerParameter("Top N per Cluster", "TopN", "Show only top N best per cluster per generation. 0 = all (default). 1 = best one per cluster.", GH_ParamAccess.item, 0);
            pManager.AddVectorParameter("Column Spacing", "Col",
                "World-space offset between columns. Default (30, 0, 0).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultColumnSpacing);
            pManager.AddVectorParameter("Row Spacing", "Row",
                "World-space offset between rows. Default (0, 0, -10).",
                GH_ParamAccess.item, PreviewLayoutTransforms.DefaultRowSpacingCompact);
            pManager.AddNumberParameter("Radius", "R", "Axis length", GH_ParamAccess.item, 1.0);
            pManager.AddIntervalParameter("Metric Domains", "MDom",
                "Expected [min, max] domain per metric axis for normalization. Order matches assembly MetricNames. If not supplied, each axis uses observed min–max (supports negative metrics).", GH_ParamAccess.list);
            pManager.AddPointParameter("Insert Point", "Pt", "Base point", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Text Height", "TxH", "Height for bakeable TextEntity labels [model units].", GH_ParamAccess.item, 0.15);
            pManager.AddPlaneParameter("Display Plane", "Disp",
                "Optional plane whose X/Y axes orient each chart's geometry. Defaults to the world XZ plane.",
                GH_ParamAccess.item);
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Axes", "Axes", "Axis lines", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Polygon", "Poly", "Data polygons", GH_ParamAccess.tree);
            pManager.AddTextParameter("Info", "Info", "Summary", GH_ParamAccess.item);
            pManager.AddGeometryParameter("Text Labels", "Txt",
                "Bakeable TextEntity annotations per chart: metric names, metric values, and cluster id. Tree path {col;row} matches Axes/Poly.",
                GH_ParamAccess.tree);
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
            DA.GetDataList(1, genList);
            DA.GetDataList(2, indList);
            DA.GetData(3, ref topN);
            if (genList.Count == 0) genList.Add(-1);
            if (indList.Count == 0) indList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allInds = indList.Contains(-1);
            var indSet = allInds ? null : new HashSet<int>(indList.Where(x => x >= 0));
            topN = Math.Max(0, topN);

            Vector3d colSpacing = PreviewLayoutTransforms.DefaultColumnSpacing;
            Vector3d rowSpacing = PreviewLayoutTransforms.DefaultRowSpacingCompact;
            double radius = 1.0, textHeight = 0.15;
            Point3d insertPt = Point3d.Origin;
            var rawDomains = new List<GH_Interval>();
            var domains = new List<Interval>();
            DA.GetDataList(7, rawDomains);
            if (rawDomains.Count > 0)
                domains = rawDomains.Select(d => d.Value).ToList();
            DA.GetData(4, ref colSpacing);
            DA.GetData(5, ref rowSpacing);
            DA.GetData(6, ref radius);
            DA.GetData(8, ref insertPt);
            DA.GetData(9, ref textHeight);
            Plane displayPlane = PreviewLayoutTransforms.GetOptionalDisplayPlane(DA, 10);
            if (radius <= 0) radius = 1.0;
            textHeight = Math.Max(0.01, textHeight);

            var metricNames = assembly.MetricNames ?? new List<string>();
            if (metricNames.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly needs at least 2 metric names.");
                return;
            }

            int numAxes = metricNames.Count;
            bool hasDomains = domains != null && domains.Count >= numAxes;

            // Per-axis normalization: domains or observed max
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
                var axisLo = new double[numAxes];
                var axisHi = new double[numAxes];
                for (int m = 0; m < numAxes; m++)
                {
                    axisLo[m] = double.PositiveInfinity;
                    axisHi[m] = double.NegativeInfinity;
                }

                foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
                {
                    if (!allGens && !genList.Contains(gen.Generation)) continue;
                    for (int i = 0; i < (gen.Individuals?.Count ?? 0); i++)
                    {
                        if (!allInds && (indSet != null && !indSet.Contains(i))) continue;
                        var vals = gen.Individuals[i].AllMetrics();
                        if (vals.Count < numAxes) continue;
                        for (int m = 0; m < numAxes; m++)
                        {
                            double v = vals[m];
                            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                            if (v < axisLo[m]) axisLo[m] = v;
                            if (v > axisHi[m]) axisHi[m] = v;
                        }
                    }
                }

                const double epsSpan = 1e-15;
                for (int m = 0; m < numAxes; m++)
                {
                    if (double.IsInfinity(axisLo[m]) || double.IsInfinity(axisHi[m]))
                    {
                        axisMin[m] = 0;
                        axisRange[m] = 1.0;
                        continue;
                    }

                    axisMin[m] = axisLo[m];
                    double span = axisHi[m] - axisLo[m];
                    if (span <= epsSpan)
                    {
                        axisMin[m] = axisHi[m] - 0.5;
                        axisRange[m] = 1.0;
                    }
                    else
                        axisRange[m] = span;
                }
            }

            var axisDirs = new Vector3d[numAxes];
            for (int m = 0; m < numAxes; m++)
            {
                double ang = 2.0 * Math.PI * m / numAxes - Math.PI / 2.0;
                axisDirs[m] = new Vector3d(Math.Cos(ang), Math.Sin(ang), 0);
            }

            var axesTree = new GH_Structure<GH_Line>();
            var polyTree = new GH_Structure<GH_Curve>();
            var textTree = new GH_Structure<GH_TextEntity>();
            int col = 0;
            int totalCharts = 0;

            // Build elite set when Top N > 0
            var eliteSet = new HashSet<(int gen, int ind)>();
            if (topN > 0)
            {
                foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
                {
                    if (!allGens && !genList.Contains(gen.Generation)) continue;
                    var candidates = new List<(int indIdx, double fit, int clust)>();
                    for (int i = 0; i < (gen.Individuals?.Count ?? 0); i++)
                    {
                        if (!allInds && (indSet != null && !indSet.Contains(i))) continue;
                        var ind = gen.Individuals[i];
                        if (ind == null || ind.Fitness >= double.MaxValue * 0.5) continue;
                        candidates.Add((i, ind.Fitness, ind.ClustGrp));
                    }
                    foreach (var grp in candidates.GroupBy(c => c.clust))
                    {
                        foreach (var x in grp.OrderBy(c => c.fit).Take(topN))
                            eliteSet.Add((gen.Generation, x.indIdx));
                    }
                }
            }

            foreach (var gen in assembly.Generations ?? new List<AssemblyGeneration>())
            {
                if (!allGens && !genList.Contains(gen.Generation)) continue;
                int row = 0;
                for (int indIdx = 0; indIdx < gen.Individuals.Count; indIdx++)
                {
                    if (!allInds && (indSet == null || !indSet.Contains(indIdx))) continue;
                    if (topN > 0 && !eliteSet.Contains((gen.Generation, indIdx))) continue;
                    var ind = gen.Individuals[indIdx];
                    var vals = ind.AllMetrics();
                    if (vals.Count < numAxes) continue;

                    Point3d c = insertPt + col * colSpacing + row * rowSpacing;
                    Transform cellXf = PreviewLayoutTransforms.GetCellOrientTransform(displayPlane, c);
                    GH_Path outPath = new GH_Path(col, row);

                    var polygonPts = new List<Point3d>();
                    for (int m = 0; m < numAxes; m++)
                    {
                        Line axisLn = new Line(c, c + axisDirs[m] * radius);
                        axisLn.Transform(cellXf);
                        axesTree.Append(new GH_Line(axisLn), outPath);
                        double norm = axisRange[m] > 0 && !double.IsNaN(vals[m]) && !double.IsInfinity(vals[m])
                            ? (vals[m] - axisMin[m]) / axisRange[m]
                            : 0;
                        double clampedNorm = double.IsNaN(vals[m]) || double.IsInfinity(vals[m])
                            ? 0
                            : Math.Max(0, Math.Min(1, norm));
                        polygonPts.Add(c + axisDirs[m] * (radius * clampedNorm));

                        Vector3d xdir = axisDirs[m];
                        Vector3d ydir = new Vector3d(-axisDirs[m].Y, axisDirs[m].X, 0);
                        bool leftSide = axisDirs[m].X < -1e-10 || (Math.Abs(axisDirs[m].X) < 1e-10 && axisDirs[m].Y < 0);
                        if (leftSide) { xdir = -xdir; ydir = -ydir; }
                        double labelOffset = radius + textHeight * 3.5;
                        Point3d labelPt = c + axisDirs[m] * labelOffset;
                        Plane namePl = new Plane(labelPt, xdir, ydir);
                        namePl.Transform(cellXf);
                        textTree.Append(CreateTextEntity(namePl, metricNames[m], textHeight), outPath);

                        Plane valPl = new Plane(labelPt - ydir * textHeight * 1.3, xdir, ydir);
                        valPl.Transform(cellXf);
                        textTree.Append(CreateTextEntity(valPl,
                            string.Format("{0:F3} ({1:F2})", vals[m], clampedNorm), textHeight), outPath);
                    }

                    if (polygonPts.Count >= 3)
                    {
                        for (int pi = 0; pi < polygonPts.Count; pi++)
                        {
                            Point3d pt = polygonPts[pi];
                            pt.Transform(cellXf);
                            polygonPts[pi] = pt;
                        }
                        polygonPts.Add(polygonPts[0]);
                        polyTree.Append(new GH_Curve(new Polyline(polygonPts).ToNurbsCurve()), outPath);
                    }

                    Plane clPl = new Plane(c - new Vector3d(0, radius * 1.25, 0), Vector3d.XAxis, Vector3d.YAxis);
                    clPl.Transform(cellXf);
                    textTree.Append(CreateTextEntity(clPl,
                        string.Format("Cluster {0}  G{1}  I{2}", ind.ClustGrp, gen.Generation, indIdx),
                        textHeight * 1.1), outPath);

                    row++;
                    totalCharts++;
                }
                col++;
            }

            string normMode = hasDomains ? "user domains" : "observed min–max";
            DA.SetDataTree(0, axesTree);
            DA.SetDataTree(1, polyTree);
            DA.SetData(2, string.Format(
                "Radar from Assembly: {0} charts, {1} metrics. Normalization: {2}. Text labels: {3} entities (bakeable).",
                totalCharts, numAxes, normMode, textTree.DataCount));
            DA.SetDataTree(3, textTree);
        }

        private static GH_TextEntity CreateTextEntity(Plane plane, string text, double height)
        {
            var te = new TextEntity
            {
                Plane = plane,
                PlainText = text ?? string.Empty,
                TextHeight = height,
                Justification = TextJustification.MiddleCenter
            };
            return new GH_TextEntity(te);
        }

        protected override Bitmap Icon => Properties.Resources.icons_CAT_DataPreview;
        public override Guid ComponentGuid => new Guid("B2C3D4E5-F6A7-8901-BCDE-F01234567891");
    }
}
