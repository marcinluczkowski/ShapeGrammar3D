using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Outputs the convex-hull mesh used by Shape Metric 13 (ConvexHullVolume)
    /// for individuals in an SG Assembly. Uses <see cref="ShapeMetrics.ConvexHullMesh"/>,
    /// so the mesh is exactly the same shape whose volume feeds metric 13.
    /// </summary>
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class AssemblyConvexHullMesh : GH_Component
    {
        public AssemblyConvexHullMesh()
            : base("Assembly Convex Hull Mesh", "AsmCHull",
                "Extracts the convex-hull mesh (Shape Metric 13) from individuals stored in an SG Assembly. " +
                "DataTree: one branch per individual when Ind = -1.",
                UT.CAT, UT.GR_ASSEM)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Asm",
                "SG Assembly from a Grammar Interpreter (GI_FromSg, GI_FromBnd, GI_Large, …).",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation id (AssemblyGeneration.Generation). -1 = last stored generation.",
                GH_ParamAccess.item, -1);
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual index within that generation (0-based). -1 = all individuals in the generation.",
                GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Method", "M",
                "Mesh method: 0 = Convex Hull (strict metric-13), 1 = ShrinkWrap (follows concavity/detail).",
                GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Detail Ratio", "Det",
                "Detail level for ShrinkWrap as fraction of bbox diagonal (e.g. 0.01~0.05). Smaller = finer.",
                GH_ParamAccess.item, 0.02);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Hull mesh", "Mesh",
                "Convex hull mesh (same mesh that ShapeMetric 13 uses to compute volume). " +
                "DataTree: one branch per individual when Ind = -1.",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Volume", "V",
                "Volume of each hull mesh (matches Shape Metric 13 for that individual).",
                GH_ParamAccess.tree);
            pManager.AddTextParameter("Summary", "Sum",
                "Short summary of what was exported.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_SGAssembly ghAsm = null;
            if (!DA.GetData(0, ref ghAsm) || ghAsm?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Valid Assembly is required.");
                return;
            }

            int genReq = -1;
            int indReq = 0;
            int method = 0;
            double detailRatio = 0.02;
            DA.GetData(1, ref genReq);
            DA.GetData(2, ref indReq);
            DA.GetData(3, ref method);
            DA.GetData(4, ref detailRatio);

            var asm = ghAsm.Value;
            var gens = asm.Generations;
            if (gens == null || gens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                DA.SetDataTree(0, new GH_Structure<GH_Mesh>());
                DA.SetDataTree(1, new GH_Structure<GH_Number>());
                DA.SetData(2, "No data.");
                return;
            }

            var gen = ResolveGeneration(gens, genReq);
            if (gen == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    string.Format(CultureInfo.InvariantCulture,
                        "Generation {0} not found. Use a valid AssemblyGeneration.Generation value, a list index 0..{1}, or -1 for the last generation.",
                        genReq, gens.Count - 1));
                return;
            }

            if (gen.Individuals == null || gen.Individuals.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selected generation has no individuals.");
                DA.SetDataTree(0, new GH_Structure<GH_Mesh>());
                DA.SetDataTree(1, new GH_Structure<GH_Number>());
                DA.SetData(2, "No individuals.");
                return;
            }

            var meshTree = new GH_Structure<GH_Mesh>();
            var volTree = new GH_Structure<GH_Number>();

            if (indReq < 0)
            {
                int emitted = 0;
                for (int i = 0; i < gen.Individuals.Count; i++)
                {
                    var path = new GH_Path(i);
                    if (AppendHullFor(gen.Individuals[i], path, meshTree, volTree, method, detailRatio)) emitted++;
                }

                DA.SetDataTree(0, meshTree);
                DA.SetDataTree(1, volTree);
                DA.SetData(2,
                    string.Format(CultureInfo.InvariantCulture,
                        "Generation id {0}: {1}/{2} individuals produced a hull mesh.",
                        gen.Generation, emitted, gen.Individuals.Count));
            }
            else
            {
                if (indReq >= gen.Individuals.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        string.Format(CultureInfo.InvariantCulture,
                            "Individual index {0} is out of range (0..{1}).",
                            indReq, gen.Individuals.Count - 1));
                    return;
                }

                var path = new GH_Path(0);
                bool ok = AppendHullFor(gen.Individuals[indReq], path, meshTree, volTree, method, detailRatio);

                DA.SetDataTree(0, meshTree);
                DA.SetDataTree(1, volTree);
                DA.SetData(2, ok
                    ? string.Format(CultureInfo.InvariantCulture,
                        "Generation id {0}, individual {1}: mesh exported (method={2}).",
                        gen.Generation, indReq, method)
                    : string.Format(CultureInfo.InvariantCulture,
                        "Generation id {0}, individual {1}: mesh could not be computed (insufficient points or invalid mesh).",
                        gen.Generation, indReq));
            }
        }

        private static bool AppendHullFor(AssemblyIndividual ind, GH_Path path,
            GH_Structure<GH_Mesh> meshTree, GH_Structure<GH_Number> volTree,
            int method, double detailRatio)
        {
            if (ind?.Shape == null) return false;

            Mesh mesh = method == 1
                ? ShapeMetrics.ShrinkWrapMesh(ind.Shape, detailRatio)
                : ShapeMetrics.ConvexHullMesh(ind.Shape);
            if (mesh == null || !mesh.IsValid) return false;

            meshTree.Append(new GH_Mesh(mesh), path);
            double volume = 0.0;
            try
            {
                var vp = VolumeMassProperties.Compute(mesh);
                volume = Math.Abs(vp?.Volume ?? 0.0);
            }
            catch { }
            volTree.Append(new GH_Number(volume), path);
            return true;
        }

        private static AssemblyGeneration ResolveGeneration(List<AssemblyGeneration> gens, int genReq)
        {
            if (gens == null || gens.Count == 0)
                return null;
            if (genReq == -1)
                return gens[gens.Count - 1];

            var byId = gens.FirstOrDefault(g => g.Generation == genReq);
            if (byId != null)
                return byId;

            if (genReq >= 0 && genReq < gens.Count)
                return gens[genReq];

            return null;
        }

        protected override Bitmap Icon => Properties.Resources.icons_CAT_DataPreview;
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid => new Guid("E4D9B2F3-5A17-46D1-8C9A-7E1F0B2C4D88");
    }
}
