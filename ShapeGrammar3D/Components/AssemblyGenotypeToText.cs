using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Exports genotype data from an SG Assembly as Grasshopper lists (DataTree):
    /// integer chromosome and double chromosome per individual, matching
    /// <see cref="SG_Genotype.IntGenes"/> / <see cref="SG_Genotype.DGenes"/> storage
    /// in <see cref="AssemblyIndividual.Chromosome"/> / <see cref="AssemblyIndividual.ChromosomeParam"/>.
    /// </summary>
    public class AssemblyGenotypeToText : GH_Component
    {
        public AssemblyGenotypeToText()
            : base("Assembly Genotype to List", "AsmGTxt",
                "Exports each individual's integer and double chromosome from an SG Assembly as list outputs (DataTree; one branch per individual when Ind = -1).",
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
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Int genes", "IntG",
                "Integer chromosome as a list (DataTree; branch {i} = individual i when Ind = -1).",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Double genes", "DblG",
                "Double chromosome as a list (DataTree; same branch layout as Int genes).",
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
            DA.GetData(1, ref genReq);
            DA.GetData(2, ref indReq);

            var asm = ghAsm.Value;
            var gens = asm.Generations;
            if (gens == null || gens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                DA.SetDataTree(0, new GH_Structure<GH_Integer>());
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
                DA.SetDataTree(0, new GH_Structure<GH_Integer>());
                DA.SetDataTree(1, new GH_Structure<GH_Number>());
                DA.SetData(2, "No individuals.");
                return;
            }

            var intTree = new GH_Structure<GH_Integer>();
            var dblTree = new GH_Structure<GH_Number>();

            if (indReq < 0)
            {
                for (int i = 0; i < gen.Individuals.Count; i++)
                {
                    var path = new GH_Path(i);
                    var ind = gen.Individuals[i];
                    AppendInts(intTree, path, ind?.Chromosome);
                    AppendDoubles(dblTree, path, ind?.ChromosomeParam);
                }

                DA.SetDataTree(0, intTree);
                DA.SetDataTree(1, dblTree);
                DA.SetData(2,
                    string.Format(CultureInfo.InvariantCulture,
                        "Generation id {0}: {1} individuals, int length (first) = {2}, double length = {3}",
                        gen.Generation, gen.Individuals.Count,
                        gen.Individuals[0].Chromosome?.Count ?? 0,
                        gen.Individuals[0].ChromosomeParam?.Count ?? 0));
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

                var ind = gen.Individuals[indReq];
                var path = new GH_Path(0);
                AppendInts(intTree, path, ind?.Chromosome);
                AppendDoubles(dblTree, path, ind?.ChromosomeParam);

                DA.SetDataTree(0, intTree);
                DA.SetDataTree(1, dblTree);
                DA.SetData(2,
                    string.Format(CultureInfo.InvariantCulture,
                        "Generation id {0}, individual {1}: int count = {2}, double count = {3}",
                        gen.Generation, indReq,
                        ind.Chromosome?.Count ?? 0,
                        ind.ChromosomeParam?.Count ?? 0));
            }
        }

        private static void AppendInts(GH_Structure<GH_Integer> tree, GH_Path path, IList<int> values)
        {
            if (values == null) return;
            foreach (int v in values)
                tree.Append(new GH_Integer(v), path);
        }

        private static void AppendDoubles(GH_Structure<GH_Number> tree, GH_Path path, IList<double> values)
        {
            if (values == null) return;
            foreach (double v in values)
                tree.Append(new GH_Number(v), path);
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

        protected override Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("A4C8E2F1-6B3D-4E9A-8C7D-1F2E3D4B5A60");
    }
}
