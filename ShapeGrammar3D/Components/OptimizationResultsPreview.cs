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
    /// Renders an <see cref="OptimizationResults"/> bundle (from GI_LargeJson Reader) into:
    ///   • a 3D Pareto view (Displacement × Utilization × Feasibility),
    ///   • three 2D projections (Disp-Util, Disp-Feas, Util-Feas),
    ///   • per-cluster convergence,
    /// each with axis/grid lines and bakeable text labels. All scatter trees use a
    /// {view; generation; cluster} path so every cluster sits in its own sub-branch.
    ///
    /// When an SG Assembly is connected, the scatter markers are replaced by scaled
    /// structure miniatures placed at each individual's coordinate.
    /// Heavy JSON parsing already happened in the reader, so re-rendering here is cheap.
    /// </summary>
    public class GI_OptimizationPreview : GH_Component
    {
        // View indices used as the top tree dimension.
        private const int V_3D = 0, V_DU = 1, V_DF = 2, V_UF = 3;
        private const int CROSS_BASE = 100;   // marker lines: {CROSS_BASE + view; gen}
        private const int CONV_BRANCH = 200;  // convergence lines: {CONV_BRANCH; cluster}

        public GI_OptimizationPreview()
          : base("GI_Opti Preview", "GI_Opti",
              "Preview Optimization Results: 3D Pareto (Disp×Util×Feas), 3 projections (DU/DF/UF) and convergence, with lines + bakeable labels. Connect an Assembly to swap points for scaled structure miniatures.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_OptimizationResults(), "Results", "Res", "Optimization Results from GI_LargeJson Reader.", GH_ParamAccess.item); // 0
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly",
                "Optional. When connected, individuals with a rebuilt model are drawn as scaled structure miniatures instead of point markers. Build with GI_LargeJson Reader (Build Models = true).",
                GH_ParamAccess.item);                                                                                                                       // 1
            pManager.AddPlaneParameter("Plane", "Pln", "Base plane (origin = bottom-left of the 3D view).", GH_ParamAccess.item, Plane.WorldXY);             // 2
            pManager.AddIntegerParameter("Generation", "Gen", "Which generation(s). -1 = all.", GH_ParamAccess.list, -1);                                    // 3
            pManager.AddIntegerParameter("Clusters", "Cls", "Which cluster(s). -1 = all.", GH_ParamAccess.list, -1);                                         // 4
            pManager.AddNumberParameter("Width", "W", "Per-view graph width.", GH_ParamAccess.item, 10.0);                                                   // 5
            pManager.AddNumberParameter("Height", "H", "Per-view graph height.", GH_ParamAccess.item, 6.0);                                                  // 6
            pManager.AddNumberParameter("Depth", "D", "3D view depth (Feasibility axis).", GH_ParamAccess.item, 6.0);                                        // 7
            pManager.AddNumberParameter("Text Height", "TxH", "Bakeable label text height [model units].", GH_ParamAccess.item, 0.12);                       // 8
            pManager.AddNumberParameter("Point Size", "PtSz", "Cross marker size.", GH_ParamAccess.item, 0.08);                                              // 9
            pManager.AddNumberParameter("View Gap", "Gap", "Spacing between views. Empty/0 = max(W,H) * 0.6.", GH_ParamAccess.item, 0.0);                     // 10
            pManager.AddNumberParameter("Miniature Size", "Mini", "Target size of each structure miniature (when Assembly connected). Empty/0 = min(W,H) * 0.12.", GH_ParamAccess.item, 0.0); // 11
            pManager.AddBooleanParameter("Unitized", "Unit",
                "Axis tick labels: false = real data values, true = unitized 0..1 (data normalized per axis). Geometry layout is unchanged; only the tick text differs.",
                GH_ParamAccess.item, false);                                                                                                                // 12

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "Info", "Summary.", GH_ParamAccess.item);                                                              // 0
            pManager.AddPointParameter("Points", "Pts", "Scatter points per {view; gen; cluster}. View 0=3D, 1=DispUtil, 2=DispFeas, 3=UtilFeas. Omitted where a miniature is drawn.", GH_ParamAccess.tree); // 1
            pManager.AddColourParameter("Point Colours", "PCol", "Cluster colour per point (matches Points).", GH_ParamAccess.tree);                 // 2
            pManager.AddLineParameter("Miniatures", "Mini", "Scaled structure lines per {view; gen; cluster} (only when Assembly connected).", GH_ParamAccess.tree); // 3
            pManager.AddColourParameter("Miniature Colours", "MCol", "Cluster colour per miniature line (matches Miniatures).", GH_ParamAccess.tree); // 4
            pManager.AddLineParameter("Marker Lines", "Mk", "Point cross markers per {CROSS_BASE+view; gen} (omitted where a miniature is drawn).", GH_ParamAccess.tree); // 5
            pManager.AddColourParameter("Marker Colours", "MkCol", "Colour per marker line.", GH_ParamAccess.tree);                                  // 6
            pManager.AddLineParameter("Axis Lines", "Ax", "Axis system: frames, grid, ticks and cluster legend swatches (no data/convergence). Bakeable.", GH_ParamAccess.tree); // 7
            pManager.AddColourParameter("Axis Colours", "AxCol", "Colour per axis line.", GH_ParamAccess.tree);                                      // 8
            pManager.AddLineParameter("Convergence Lines", "CLn", "Per-cluster convergence polyline segments (separated from the axis system). Bakeable.", GH_ParamAccess.tree); // 9
            pManager.AddColourParameter("Convergence Colours", "CLCol", "Colour per convergence line.", GH_ParamAccess.tree);                        // 10
            pManager.AddGeometryParameter("Labels", "Txt", "Bakeable TextEntity labels: axis names, tick values, cluster legend, titles.", GH_ParamAccess.tree); // 11
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_OptimizationResults ghRes = null;
            if (!DA.GetData(0, ref ghRes) || ghRes?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Optimization Results required.");
                return;
            }
            var res = ghRes.Value;

            GH_SGAssembly ghAssembly = null;
            DA.GetData(1, ref ghAssembly);
            var assembly = ghAssembly?.Value;

            Plane plane = Plane.WorldXY;
            var genList = new List<int>();
            var clusterList = new List<int>();
            double graphW = 10.0, graphH = 6.0, graphD = 6.0, textH = 0.12, ptSize = 0.08, viewGap = 0.0, miniSize = 0.0;
            bool unitized = false;

            DA.GetData(2, ref plane);
            DA.GetDataList(3, genList);
            DA.GetDataList(4, clusterList);
            DA.GetData(5, ref graphW);
            DA.GetData(6, ref graphH);
            DA.GetData(7, ref graphD);
            DA.GetData(8, ref textH);
            DA.GetData(9, ref ptSize);
            DA.GetData(10, ref viewGap);
            DA.GetData(11, ref miniSize);
            DA.GetData(12, ref unitized);

            if (genList.Count == 0) genList.Add(-1);
            if (clusterList.Count == 0) clusterList.Add(-1);
            bool allGens = genList.Contains(-1);
            bool allClusters = clusterList.Contains(-1);
            graphW = Math.Max(1.0, graphW);
            graphH = Math.Max(1.0, graphH);
            graphD = Math.Max(1.0, graphD);
            textH = Math.Max(0.01, textH);
            ptSize = Math.Max(0.001, ptSize);
            if (viewGap <= 0) viewGap = Math.Max(graphW, graphH) * 0.6;
            if (miniSize <= 0) miniSize = Math.Min(graphW, graphH) * 0.12;

            int numObj = Math.Clamp(res.NumObjectives, 1, 3);
            int clusterSpan = res.ClusterSpan();

            // Collect selected records with displacement / utilization / feasibility values.
            var records = new List<Rec>();
            foreach (var p in res.Points)
            {
                if (!allGens && !genList.Contains(p.Generation)) continue;
                if (!allClusters && !clusterList.Contains(p.Cluster)) continue;

                double disp = numObj >= 2
                    ? p.Fitness
                    : (p.Objectives != null && p.Objectives.Count > 0 ? p.Objectives[0] : p.Fitness);
                if (IsInvalid(disp)) continue;

                double util = p.Objectives != null && p.Objectives.Count > 1 ? p.Objectives[1] : 0.0;
                double feas = !IsInvalid(p.Feas)
                    ? p.Feas
                    : (p.Objectives != null && p.Objectives.Count > 2 ? p.Objectives[2] : 0.0);
                if (IsInvalid(util)) util = 0.0;
                if (IsInvalid(feas)) feas = 0.0;

                records.Add(new Rec
                {
                    Gen = p.Generation,
                    Index = p.IndexInGen,
                    Cluster = p.Cluster,
                    Id = p.Id,
                    Disp = disp,
                    Util = util,
                    Feas = feas
                });
            }

            var pts = new GH_Structure<GH_Point>();
            var ptCols = new GH_Structure<GH_Colour>();
            var minis = new GH_Structure<GH_Line>();
            var miniCols = new GH_Structure<GH_Colour>();
            var markerLines = new GH_Structure<GH_Line>();
            var markerCols = new GH_Structure<GH_Colour>();
            var axisLines = new GH_Structure<GH_Line>();
            var axisCols = new GH_Structure<GH_Colour>();
            var convLines = new GH_Structure<GH_Line>();
            var convCols = new GH_Structure<GH_Colour>();
            var labels = new GH_Structure<GH_TextEntity>();

            if (records.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No points match Generation/Cluster filters.");
                DA.SetData(0, "No points match filters.");
                DA.SetDataTree(1, pts);
                DA.SetDataTree(2, ptCols);
                DA.SetDataTree(3, minis);
                DA.SetDataTree(4, miniCols);
                DA.SetDataTree(5, markerLines);
                DA.SetDataTree(6, markerCols);
                DA.SetDataTree(7, axisLines);
                DA.SetDataTree(8, axisCols);
                DA.SetDataTree(9, convLines);
                DA.SetDataTree(10, convCols);
                DA.SetDataTree(11, labels);
                return;
            }

            // Global axis ranges (stable across views and generations).
            double minD = records.Min(r => r.Disp), maxD = records.Max(r => r.Disp);
            double minU = records.Min(r => r.Util), maxU = records.Max(r => r.Util);
            double minF = records.Min(r => r.Feas), maxF = records.Max(r => r.Feas);
            PadRange(ref minD, ref maxD);
            PadRange(ref minU, ref maxU);
            PadRange(ref minF, ref maxF);
            double[] axMin = { minD, minU, minF };
            double[] axRng = { Rng(minD, maxD), Rng(minU, maxU), Rng(minF, maxF) };
            string[] axName = { "Displacement", "Utilization", "Feasibility" };

            Point3d O = plane.Origin;
            Vector3d xDir = plane.XAxis, yDir = plane.YAxis, zDir = plane.ZAxis;
            double step = graphW + viewGap;

            // View layout (each view laid out left → right; convergence below the 3D view).
            var views = new List<ViewDef>
            {
                new ViewDef { Index = V_3D, Origin = O,                 AxX = 0, AxY = 1, AxZ = 2, HasZ = true,  Title = "Pareto 3D (Disp x Util x Feas)" },
                new ViewDef { Index = V_DU, Origin = O + xDir * step,     AxX = 0, AxY = 1, AxZ = -1, HasZ = false, Title = "Disp vs Util" },
                new ViewDef { Index = V_DF, Origin = O + xDir * step * 2, AxX = 0, AxY = 2, AxZ = -1, HasZ = false, Title = "Disp vs Feas" },
                new ViewDef { Index = V_UF, Origin = O + xDir * step * 3, AxX = 1, AxY = 2, AxZ = -1, HasZ = false, Title = "Util vs Feas" },
            };

            foreach (var view in views)
            {
                BuildFrame(axisLines, axisCols, labels, new GH_Path(view.Index), view.Origin, xDir, yDir, zDir,
                    graphW, graphH, graphD, view.HasZ,
                    axMin[view.AxX], axRng[view.AxX], axMin[view.AxY], axRng[view.AxY],
                    view.HasZ ? axMin[view.AxZ] : 0, view.HasZ ? axRng[view.AxZ] : 1,
                    textH, axName[view.AxX], axName[view.AxY], view.HasZ ? axName[view.AxZ] : "", view.Title, unitized);
            }

            // Assembly lookup for miniatures.
            var byId = new Dictionary<string, AssemblyIndividual>();
            var byGenIdx = new Dictionary<(int, int), AssemblyIndividual>();
            if (assembly?.Generations != null)
            {
                foreach (var gen in assembly.Generations)
                {
                    if (gen.Individuals == null) continue;
                    for (int i = 0; i < gen.Individuals.Count; i++)
                    {
                        var ind = gen.Individuals[i];
                        if (ind == null) continue;
                        byGenIdx[(gen.Generation, i)] = ind;
                        if (!string.IsNullOrEmpty(ind.Id)) byId[ind.Id] = ind;
                    }
                }
            }
            bool useMini = byGenIdx.Count > 0;
            int miniDrawn = 0;

            // Scatter points / miniatures per view.
            foreach (var r in records)
            {
                double[] vals = { r.Disp, r.Util, r.Feas };
                Color col = GetClusterColour(r.Cluster, clusterSpan);
                var dataPath = new GH_Path(0, r.Gen, r.Cluster); // placeholder, rebuilt per view below

                AssemblyIndividual ind = null;
                if (useMini)
                {
                    if (!string.IsNullOrEmpty(r.Id)) byId.TryGetValue(r.Id, out ind);
                    if (ind == null) byGenIdx.TryGetValue((r.Gen, r.Index), out ind);
                }
                var miniLines = ind != null ? ExtractShapeLines(ind) : null;
                bool hasMini = miniLines != null && miniLines.Count > 0;

                foreach (var view in views)
                {
                    double fx = Frac(vals[view.AxX], axMin[view.AxX], axRng[view.AxX]);
                    double fy = Frac(vals[view.AxY], axMin[view.AxY], axRng[view.AxY]);
                    Point3d pt = view.Origin + xDir * (fx * graphW) + yDir * (fy * graphH);
                    if (view.HasZ)
                    {
                        double fz = Frac(vals[view.AxZ], axMin[view.AxZ], axRng[view.AxZ]);
                        pt += zDir * (fz * graphD);
                    }

                    var path = new GH_Path(view.Index, r.Gen, r.Cluster);
                    if (hasMini)
                    {
                        PlaceMiniature(miniLines, pt, miniSize, col, path, minis, miniCols);
                    }
                    else
                    {
                        pts.Append(new GH_Point(pt), path);
                        ptCols.Append(new GH_Colour(col), path);
                        AppendCross(markerLines, markerCols, pt, xDir, yDir, zDir, ptSize, view.HasZ, col,
                            new GH_Path(CROSS_BASE + view.Index, r.Gen));
                    }
                }
                if (hasMini) miniDrawn++;
            }

            // Cluster legend on the 3D view.
            var presentClusters = records.Select(r => r.Cluster).Distinct().OrderBy(c => c).ToList();
            for (int i = 0; i < presentClusters.Count; i++)
            {
                int cl = presentClusters[i];
                Color col = GetClusterColour(cl, clusterSpan);
                double legendY = graphH - i * textH * 1.8;
                Point3d legendPt = O + xDir * (graphW + textH * 1.5) + yDir * legendY;
                axisLines.Append(new GH_Line(new Line(legendPt - xDir * (textH * 1.2), legendPt - xDir * (textH * 0.2))), new GH_Path(V_3D));
                axisCols.Append(new GH_Colour(col), new GH_Path(V_3D));
                labels.Append(MakeLabel(legendPt, xDir, yDir, string.Format("C{0}", cl), textH * 0.9, col), new GH_Path(V_3D));
            }

            // Convergence below the 3D view: frame → axis outputs, polylines → convergence outputs.
            int convCount = BuildConvergence(res, presentClusters, allClusters, clusterSpan,
                O - yDir * (graphH + viewGap), xDir, yDir, graphW, graphH, textH,
                axisLines, axisCols, convLines, convCols, labels);

            string info = string.Format(
                "Opti Preview ({0} obj)\nPoints: {1}  Clusters: {2}  Gens: {3}\nViews: 3D (D x U x F) + DU + DF + UF; convergence series: {4}\nMiniatures: {5}\nAxis ticks: {6}\nLabels: {7} (bakeable)\nTree paths: {{view; gen; cluster}}  (view 0=3D, 1=DU, 2=DF, 3=UF)",
                numObj, records.Count, presentClusters.Count,
                allGens ? "all" : string.Join(",", genList), convCount,
                useMini ? string.Format("{0} drawn (Assembly connected)", miniDrawn) : "off (connect Assembly)",
                unitized ? "unitized 0..1" : "real values",
                labels.DataCount);

            DA.SetData(0, info);
            DA.SetDataTree(1, pts);
            DA.SetDataTree(2, ptCols);
            DA.SetDataTree(3, minis);
            DA.SetDataTree(4, miniCols);
            DA.SetDataTree(5, markerLines);
            DA.SetDataTree(6, markerCols);
            DA.SetDataTree(7, axisLines);
            DA.SetDataTree(8, axisCols);
            DA.SetDataTree(9, convLines);
            DA.SetDataTree(10, convCols);
            DA.SetDataTree(11, labels);
        }

        private static void BuildFrame(
            GH_Structure<GH_Line> lines, GH_Structure<GH_Colour> cols, GH_Structure<GH_TextEntity> labels,
            GH_Path path, Point3d origin, Vector3d xDir, Vector3d yDir, Vector3d zDir,
            double w, double h, double d, bool hasZ,
            double minX, double rngX, double minY, double rngY, double minZ, double rngZ,
            double textH, string xName, string yName, string zName, string title, bool unitized)
        {
            Color axisColor = Color.FromArgb(120, 120, 120);
            Color gridColor = Color.FromArgb(225, 225, 225);
            Color borderColor = Color.FromArgb(200, 200, 200);
            Color textColor = Color.FromArgb(60, 60, 60);

            AddLine(lines, cols, path, origin, origin + xDir * w, axisColor);
            AddLine(lines, cols, path, origin, origin + yDir * h, axisColor);
            Point3d topLeft = origin + yDir * h;
            Point3d botRight = origin + xDir * w;
            AddLine(lines, cols, path, topLeft, topLeft + xDir * w, borderColor);
            AddLine(lines, cols, path, botRight, botRight + yDir * h, borderColor);

            if (hasZ)
            {
                AddLine(lines, cols, path, origin, origin + zDir * d, axisColor);
                labels.Append(MakeLabel(origin + zDir * (d * 0.5) - xDir * (textH * 2), zDir, xDir, zName, textH, textColor), path);
            }

            const int ticks = 5;
            for (int t = 0; t <= ticks; t++)
            {
                double frac = (double)t / ticks;
                Point3d tickBase = origin + xDir * (frac * w);
                Point3d tickEnd = tickBase - yDir * (textH * 0.3);
                AddLine(lines, cols, path, tickBase, tickEnd, axisColor);
                if (t > 0 && t < ticks) AddLine(lines, cols, path, tickBase, tickBase + yDir * h, gridColor);
                labels.Append(MakeLabel(tickEnd - yDir * (textH * 1.0), xDir, yDir, TickText(minX, rngX, frac, unitized), textH * 0.8, textColor), path);
            }
            for (int t = 0; t <= ticks; t++)
            {
                double frac = (double)t / ticks;
                Point3d tickBase = origin + yDir * (frac * h);
                Point3d tickEnd = tickBase - xDir * (textH * 0.3);
                AddLine(lines, cols, path, tickBase, tickEnd, axisColor);
                if (t > 0 && t < ticks) AddLine(lines, cols, path, tickBase, tickBase + xDir * w, gridColor);
                labels.Append(MakeLabel(tickEnd - xDir * (textH * 1.6), xDir, yDir, TickText(minY, rngY, frac, unitized), textH * 0.8, textColor), path);
            }
            if (hasZ)
            {
                for (int t = 0; t <= ticks; t++)
                {
                    double frac = (double)t / ticks;
                    Point3d tickBase = origin + zDir * (frac * d);
                    Point3d tickEnd = tickBase - xDir * (textH * 0.3);
                    AddLine(lines, cols, path, tickBase, tickEnd, axisColor);
                    labels.Append(MakeLabel(tickEnd - xDir * (textH * 1.6), zDir, xDir, TickText(minZ, rngZ, frac, unitized), textH * 0.8, textColor), path);
                }
            }

            labels.Append(MakeLabel(origin + xDir * (w * 0.5) - yDir * (textH * 3.0), xDir, yDir, xName, textH, textColor), path);
            labels.Append(MakeLabel(origin - xDir * (textH * 5) + yDir * (h * 0.5), yDir, -xDir, yName, textH, textColor), path);
            labels.Append(MakeLabel(origin + yDir * (h + textH * 1.2), xDir, yDir, title, textH * 1.3, Color.FromArgb(40, 40, 40)), path);
        }

        private static int BuildConvergence(OptimizationResults res, List<int> presentClusters, bool allClusters, int clusterSpan,
            Point3d origin, Vector3d xDir, Vector3d yDir, double w, double h, double textH,
            GH_Structure<GH_Line> axisLines, GH_Structure<GH_Colour> axisCols,
            GH_Structure<GH_Line> convLines, GH_Structure<GH_Colour> convCols,
            GH_Structure<GH_TextEntity> labels)
        {
            if (res.Aggregates == null || res.Aggregates.Count == 0) return 0;

            var byCluster = res.Aggregates
                .Where(a => !IsInvalid(a.Best))
                .Where(a => allClusters || presentClusters.Contains(a.Cluster))
                .GroupBy(a => a.Cluster)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Generation).ToList());
            if (byCluster.Count == 0) return 0;

            var allGenIds = byCluster.Values.SelectMany(v => v.Select(a => a.Generation)).Distinct().OrderBy(x => x).ToList();
            if (allGenIds.Count < 2) return 0;
            int genMin = allGenIds.First(), genMax = allGenIds.Last();
            if (genMax == genMin) genMax = genMin + 1;

            double globalMin = byCluster.Values.SelectMany(v => v.Select(a => a.Best)).Min();
            double globalMax = byCluster.Values.SelectMany(v => v.Select(a => a.Best)).Max();
            if (Math.Abs(globalMax - globalMin) < 1e-12) globalMax = globalMin + 1.0;

            // Frame for convergence chart → axis outputs.
            Color axisColor = Color.FromArgb(120, 120, 120);
            AddLine(axisLines, axisCols, new GH_Path(CONV_BRANCH), origin, origin + xDir * w, axisColor);
            AddLine(axisLines, axisCols, new GH_Path(CONV_BRANCH), origin, origin + yDir * h, axisColor);

            int series = 0;
            foreach (var kvp in byCluster.OrderBy(k => k.Key))
            {
                var data = kvp.Value;
                if (data.Count < 2) continue;
                Color col = GetClusterColour(kvp.Key, clusterSpan);
                var path = new GH_Path(CONV_BRANCH, kvp.Key);
                for (int i = 1; i < data.Count; i++)
                {
                    Point3d p0 = ConvPoint(data[i - 1].Generation, data[i - 1].Best, genMin, genMax, globalMin, globalMax, origin, xDir, yDir, w, h);
                    Point3d p1 = ConvPoint(data[i].Generation, data[i].Best, genMin, genMax, globalMin, globalMax, origin, xDir, yDir, w, h);
                    convLines.Append(new GH_Line(new Line(p0, p1)), path);
                    convCols.Append(new GH_Colour(col), path);
                }
                series++;
            }
            if (series > 0)
            {
                labels.Append(MakeLabel(origin + yDir * (h + textH * 1.2), xDir, yDir, "Convergence (best fitness)", textH * 1.1, Color.FromArgb(40, 40, 40)), new GH_Path(CONV_BRANCH));
                labels.Append(MakeLabel(origin + xDir * (w * 0.5) - yDir * (textH * 2.5), xDir, yDir, "Generation", textH, Color.FromArgb(60, 60, 60)), new GH_Path(CONV_BRANCH));
            }
            return series;
        }

        private static Point3d ConvPoint(int gen, double best, int genMin, int genMax, double min, double max,
            Point3d origin, Vector3d xDir, Vector3d yDir, double w, double h)
        {
            double fx = genMax > genMin ? (double)(gen - genMin) / (genMax - genMin) : 0.0;
            double t = (best - min) / (max - min);
            return origin + xDir * (fx * w) + yDir * (Math.Clamp(t, 0, 1) * h);
        }

        /// <summary>Scales/translates raw structure lines so they fit a cell of <paramref name="size"/>, centred on <paramref name="at"/>.</summary>
        private static void PlaceMiniature(List<Line> rawLines, Point3d at, double size, Color col, GH_Path path,
            GH_Structure<GH_Line> minis, GH_Structure<GH_Colour> miniCols)
        {
            var bb = BoundingBox.Empty;
            foreach (var ln in rawLines) { bb.Union(ln.From); bb.Union(ln.To); }

            double scale = 1.0;
            if (bb.IsValid)
            {
                double diag = bb.Diagonal.Length;
                if (diag > 1e-9) scale = size / diag;
            }
            Vector3d translation = bb.IsValid
                ? (Vector3d)(at - bb.Center * scale)
                : (Vector3d)(at - Point3d.Origin);
            var xform = Transform.Translation(translation) * Transform.Scale(Point3d.Origin, scale);

            foreach (var ln in rawLines)
            {
                Line copy = ln;
                copy.Transform(xform);
                minis.Append(new GH_Line(copy), path);
                miniCols.Append(new GH_Colour(col), path);
            }
        }

        private static List<Line> ExtractShapeLines(AssemblyIndividual ind)
        {
            var lines = new List<Line>();
            if (ind?.Shape?.Elems == null) return lines;
            foreach (var el in ind.Shape.Elems)
            {
                if (el is SG_Elem1D e1d && e1d.Ln.Length > 1e-9)
                    lines.Add(new Line(e1d.Ln.From, e1d.Ln.To));
            }
            return lines;
        }

        private static void AppendCross(GH_Structure<GH_Line> lines, GH_Structure<GH_Colour> cols,
            Point3d pt, Vector3d xDir, Vector3d yDir, Vector3d zDir, double size, bool is3d, Color col, GH_Path path)
        {
            double half = size * 0.5;
            AddLine(lines, cols, path, pt - xDir * half, pt + xDir * half, col);
            AddLine(lines, cols, path, pt - yDir * half, pt + yDir * half, col);
            if (is3d) AddLine(lines, cols, path, pt - zDir * half, pt + zDir * half, col);
        }

        private static void AddLine(GH_Structure<GH_Line> lines, GH_Structure<GH_Colour> cols, GH_Path path,
            Point3d a, Point3d b, Color col)
        {
            lines.Append(new GH_Line(new Line(a, b)), path);
            cols.Append(new GH_Colour(col), path);
        }

        private static GH_TextEntity MakeLabel(Point3d origin, Vector3d xDir, Vector3d yDir, string text, double height, Color color)
        {
            var plane = new Plane(origin, xDir, yDir);
            var te = new TextEntity
            {
                Plane = plane,
                PlainText = text ?? string.Empty,
                TextHeight = Math.Max(0.001, height),
                Justification = TextJustification.MiddleCenter
            };
            return new GH_TextEntity(te);
        }

        private static double Frac(double v, double min, double rng) => Math.Clamp((v - min) / rng, 0, 1);
        private static double Rng(double min, double max) { double r = max - min; return r < 1e-18 ? 1.0 : r; }

        private static void PadRange(ref double min, ref double max)
        {
            if (max <= min) max = min + 1;
            double r = max - min;
            min -= r * 0.05;
            max += r * 0.05;
        }

        private static bool IsInvalid(double v) => double.IsNaN(v) || double.IsInfinity(v) || v >= double.MaxValue * 0.5;

        private static string TickText(double min, double rng, double frac, bool unitized)
            => unitized ? frac.ToString("F2") : FormatTick(min + frac * rng);

        private static string FormatTick(double v)
        {
            double abs = Math.Abs(v);
            if (abs >= 1000) return v.ToString("F0");
            if (abs >= 1) return v.ToString("F2");
            if (abs >= 0.01) return v.ToString("F4");
            if (abs < 1e-12) return "0";
            return v.ToString("E2");
        }

        private static Color GetClusterColour(int cluster, int totalClusters)
        {
            if (totalClusters <= 1) return Color.FromArgb(0, 150, 255);
            double t = (double)cluster / Math.Max(1, totalClusters - 1);
            t = Math.Clamp(t, 0.0, 1.0);
            int g = t <= 0.5 ? (int)(t / 0.5 * 255) : 255;
            int b = t <= 0.5 ? 255 : (int)((1.0 - (t - 0.5) / 0.5) * 255);
            return Color.FromArgb(0, Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        private struct Rec
        {
            public int Gen;
            public int Index;
            public int Cluster;
            public string Id;
            public double Disp;
            public double Util;
            public double Feas;
        }

        private class ViewDef
        {
            public int Index;
            public Point3d Origin;
            public int AxX;
            public int AxY;
            public int AxZ;
            public bool HasZ;
            public string Title;
        }

        protected override Bitmap Icon => Properties.Resources.icons_CAT_DataPreview;
        public override Guid ComponentGuid => new Guid("7E2A4C91-3D6B-4A18-9F2C-5B0E7D1A6C24");
    }
}
