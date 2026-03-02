using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Components.ToolboxComponents
{
    /// <summary>
    /// Decomposes a TB_Model tree into data formatted for Karamba3D input components:
    ///   - Lines     → Karamba LineToBeam
    ///   - Supports  → Karamba Support
    ///   - Loads     → Karamba PointLoad
    ///   - Sections  → Karamba CrossSection
    ///   - Materials → Karamba Material
    /// Unit convention: TB_Model uses mm internally for section dims and MPa for material;
    /// Karamba3D expects m/kN/kN·m.  Conversion flags are provided.
    /// </summary>
    public class ST_ModelToKaramba : GH_Component
    {
        public ST_ModelToKaramba()
          : base("Model→Karamba", "Mdl2K",
              "Decompose TB_Model(s) into Karamba3D-ready data",
              Common.category, Common.sub_post)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models",
                "TB_Model tree {gen}(ind) or flat list", GH_ParamAccess.tree);            // 0
            pManager.AddBooleanParameter("Convert to m/kN", "toSI",
                "True = convert section dims mm→m and loads N→kN (default true)",
                GH_ParamAccess.item, true);                                                // 1
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Lines", "Lines",
                "Element axis lines", GH_ParamAccess.tree);                                // 0
            pManager.AddTextParameter("ElemIds", "EIds",
                "Element tag per line", GH_ParamAccess.tree);                              // 1
            pManager.AddPointParameter("SupPts", "SupPt",
                "Support point locations", GH_ParamAccess.tree);                           // 2
            pManager.AddBooleanParameter("SupCond", "SupC",
                "Support conditions per point [Tx,Ty,Tz,Rx,Ry,Rz]", GH_ParamAccess.tree); // 3
            pManager.AddPointParameter("LoadPts", "LdPt",
                "Point-load locations", GH_ParamAccess.tree);                              // 4
            pManager.AddVectorParameter("LoadForces", "LdF",
                "Force vectors per load point", GH_ParamAccess.tree);                      // 5
            pManager.AddVectorParameter("LoadMoments", "LdM",
                "Moment vectors per load point", GH_ParamAccess.tree);                     // 6
            pManager.AddIntegerParameter("LoadCase", "LC",
                "Load-case index per load", GH_ParamAccess.tree);                          // 7
            pManager.AddTextParameter("SecName", "SecN",
                "Section tag per element", GH_ParamAccess.tree);                           // 8
            pManager.AddNumberParameter("SecB", "SecB",
                "Section width (B) per element", GH_ParamAccess.tree);                     // 9
            pManager.AddNumberParameter("SecH", "SecH",
                "Section height (H) per element", GH_ParamAccess.tree);                    // 10
            pManager.AddTextParameter("MatName", "MatN",
                "Material tag (unique per model)", GH_ParamAccess.tree);                   // 11
            pManager.AddNumberParameter("MatE", "E",
                "Young's modulus per material", GH_ParamAccess.tree);                      // 12
            pManager.AddNumberParameter("MatG", "G",
                "Shear modulus per material", GH_ParamAccess.tree);                        // 13
            pManager.AddNumberParameter("MatGamma", "γ",
                "Unit weight per material", GH_ParamAccess.tree);                          // 14
            pManager.AddNumberParameter("MatFy", "Fy",
                "Yield strength per material", GH_ParamAccess.tree);                       // 15
            pManager.AddTextParameter("Info", "Info",
                "Summary", GH_ParamAccess.tree);                                           // 16
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

            bool toSI = true;
            DA.GetData(1, ref toSI);

            double secScale = toSI ? 0.001 : 1.0;
            double forceScale = toSI ? 0.001 : 1.0;
            double momentScale = toSI ? 0.001 : 1.0;

            var lineTree = new GH_Structure<GH_Line>();
            var elemIdTree = new GH_Structure<GH_String>();
            var supPtTree = new GH_Structure<GH_Point>();
            var supCondTree = new GH_Structure<GH_Boolean>();
            var ldPtTree = new GH_Structure<GH_Point>();
            var ldFTree = new GH_Structure<GH_Vector>();
            var ldMTree = new GH_Structure<GH_Vector>();
            var ldLcTree = new GH_Structure<GH_Integer>();
            var secNameTree = new GH_Structure<GH_String>();
            var secBTree = new GH_Structure<GH_Number>();
            var secHTree = new GH_Structure<GH_Number>();
            var matNameTree = new GH_Structure<GH_String>();
            var matETree = new GH_Structure<GH_Number>();
            var matGTree = new GH_Structure<GH_Number>();
            var matGammaTree = new GH_Structure<GH_Number>();
            var matFyTree = new GH_Structure<GH_Number>();
            var infoTree = new GH_Structure<GH_String>();

            for (int b = 0; b < modelsTree.PathCount; b++)
            {
                GH_Path srcPath = modelsTree.Paths[b];
                var branch = (List<GH_TB_Model>)modelsTree.get_Branch(srcPath);

                for (int m = 0; m < branch.Count; m++)
                {
                    if (branch[m]?.Value == null) continue;
                    TB_Model mdl = branch[m].Value;

                    GH_Path path = new GH_Path(srcPath.Indices.Concat(new[] { m }).ToArray());

                    // ----- Elements / Lines -----
                    if (mdl.Elem1Ds != null)
                    {
                        var seenMats = new Dictionary<string, TB_Material>();

                        foreach (var e in mdl.Elem1Ds)
                        {
                            lineTree.Append(new GH_Line(e.Line), path);
                            elemIdTree.Append(new GH_String(e.Tag ?? ""), path);

                            double bDim = 0, hDim = 0;
                            string secName = "";
                            if (e.Sec is Section_Rect rect)
                            {
                                bDim = rect.B * secScale;
                                hDim = rect.H * secScale;
                                secName = rect.Tag ?? string.Format("Rect_{0}x{1}", rect.B, rect.H);
                            }
                            else if (e.Sec != null)
                            {
                                secName = e.Sec.Tag ?? "Unknown";
                            }

                            secNameTree.Append(new GH_String(secName), path);
                            secBTree.Append(new GH_Number(bDim), path);
                            secHTree.Append(new GH_Number(hDim), path);

                            if (e.Sec?.Mat != null && !seenMats.ContainsKey(e.Sec.Mat.Tag ?? ""))
                                seenMats[e.Sec.Mat.Tag ?? "default"] = e.Sec.Mat;
                        }

                        foreach (var kv in seenMats)
                        {
                            TB_Material mat = kv.Value;
                            matNameTree.Append(new GH_String(mat.Tag), path);
                            matETree.Append(new GH_Number(mat.E), path);
                            matGTree.Append(new GH_Number(mat.G), path);
                            matGammaTree.Append(new GH_Number(mat.Gamma), path);
                            matFyTree.Append(new GH_Number(mat.Fy), path);
                        }
                    }

                    // ----- Supports -----
                    if (mdl.Sups != null)
                    {
                        foreach (var sup in mdl.Sups)
                        {
                            supPtTree.Append(new GH_Point(sup.Pt), path);
                            if (sup.Conditions != null)
                            {
                                GH_Path condPath = new GH_Path(path.Indices.Concat(
                                    new[] { supPtTree.get_Branch(path).Count - 1 }).ToArray());
                                foreach (bool c in sup.Conditions)
                                    supCondTree.Append(new GH_Boolean(c), condPath);
                            }
                        }
                    }

                    // ----- Loads -----
                    if (mdl.Loads != null)
                    {
                        foreach (var ld in mdl.Loads)
                        {
                            if (ld is TB_Load_Point pl)
                            {
                                ldPtTree.Append(new GH_Point(pl.Pt), path);

                                Vector3d fv = Vector3d.Zero;
                                Vector3d mv = Vector3d.Zero;
                                if (pl.Loads != null && pl.Loads.Count >= 3)
                                {
                                    fv = new Vector3d(
                                        pl.Loads[0] * forceScale,
                                        pl.Loads[1] * forceScale,
                                        pl.Loads[2] * forceScale);
                                }
                                if (pl.Loads != null && pl.Loads.Count >= 6)
                                {
                                    mv = new Vector3d(
                                        pl.Loads[3] * momentScale,
                                        pl.Loads[4] * momentScale,
                                        pl.Loads[5] * momentScale);
                                }

                                ldFTree.Append(new GH_Vector(fv), path);
                                ldMTree.Append(new GH_Vector(mv), path);
                                ldLcTree.Append(new GH_Integer(pl.Lc ?? 0), path);
                            }
                        }
                    }

                    // ----- Info -----
                    int ne = mdl.Elem1Ds?.Count ?? 0;
                    int nn = mdl.Nodes?.Count ?? 0;
                    int ns = mdl.Sups?.Count ?? 0;
                    int nl = mdl.Loads?.Count ?? 0;
                    string info = string.Format(
                        "Elems:{0}  Nodes:{1}  Sups:{2}  Loads:{3}  SI:{4}",
                        ne, nn, ns, nl, toSI ? "m/kN" : "raw");
                    infoTree.Append(new GH_String(info), path);
                }
            }

            DA.SetDataTree(0, lineTree);
            DA.SetDataTree(1, elemIdTree);
            DA.SetDataTree(2, supPtTree);
            DA.SetDataTree(3, supCondTree);
            DA.SetDataTree(4, ldPtTree);
            DA.SetDataTree(5, ldFTree);
            DA.SetDataTree(6, ldMTree);
            DA.SetDataTree(7, ldLcTree);
            DA.SetDataTree(8, secNameTree);
            DA.SetDataTree(9, secBTree);
            DA.SetDataTree(10, secHTree);
            DA.SetDataTree(11, matNameTree);
            DA.SetDataTree(12, matETree);
            DA.SetDataTree(13, matGTree);
            DA.SetDataTree(14, matGammaTree);
            DA.SetDataTree(15, matFyTree);
            DA.SetDataTree(16, infoTree);
        }

        protected override System.Drawing.Bitmap Icon
            => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid
            => new Guid("C3D4E5F6-7A8B-9C0D-1E2F-A3B4C5D6E7F8");
    }
}
