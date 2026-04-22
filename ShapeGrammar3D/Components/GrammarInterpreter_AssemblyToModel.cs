using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Components
{
    /// <summary>
    /// Extract one TB_Model from SG Assembly.
    /// </summary>
    public class GrammarInterpreter_AssemblyToModel : GH_Component
    {
        public GrammarInterpreter_AssemblyToModel()
          : base("GI_Assembly To Model", "GI_A2M",
              "Extracts one TB_Model from SG Assembly. If Individual=-1, picks best by fitness in the selected generation.",
              UT.CAT, UT.GR_DATA_PREVIEW)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_SGAssembly(), "Assembly", "Assembly", "SG Assembly", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Generation", "Gen",
                "Generation index. -1 = last generation.", GH_ParamAccess.item, -1);
            pManager.AddIntegerParameter("Individual", "Ind",
                "Individual index. -1 = best by fitness in selected generation.", GH_ParamAccess.item, -1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Param_TB_Model(), "Model", "Model", "Selected TB_Model", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "Info", "Selection info", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_SGAssembly ghAssembly = null;
            int genReq = -1;
            int indReq = -1;

            if (!DA.GetData(0, ref ghAssembly) || ghAssembly?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly input is required.");
                return;
            }
            DA.GetData(1, ref genReq);
            DA.GetData(2, ref indReq);

            var assembly = ghAssembly.Value;
            var gens = assembly.Generations ?? new List<AssemblyGeneration>();
            if (gens.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly has no generations.");
                return;
            }

            AssemblyGeneration targetGen;
            if (genReq < 0)
                targetGen = gens.OrderBy(g => g.Generation).Last();
            else
                targetGen = gens.FirstOrDefault(g => g.Generation == genReq);

            if (targetGen == null || targetGen.Individuals == null || targetGen.Individuals.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selected generation has no individuals.");
                return;
            }

            AssemblyIndividual targetInd = null;
            int selectedIndex = -1;

            if (indReq >= 0)
            {
                if (indReq >= targetGen.Individuals.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Individual index is out of range.");
                    return;
                }
                targetInd = targetGen.Individuals[indReq];
                selectedIndex = indReq;
            }
            else
            {
                var indexed = targetGen.Individuals
                    .Select((ind, idx) => new { ind, idx })
                    .Where(x => x.ind != null && x.ind.Model != null)
                    .OrderBy(x => x.ind.Rank)
                    .ThenBy(x => x.ind.Fitness)
                    .ToList();
                if (indexed.Count > 0)
                {
                    targetInd = indexed[0].ind;
                    selectedIndex = indexed[0].idx;
                }
            }

            if (targetInd?.Model == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No TB_Model found for selected individual.");
                return;
            }

            TB_Model outModel = targetInd.Model.DeepCopy();
            DA.SetData(0, new GH_TB_Model(outModel));
            DA.SetData(1, $"Gen={targetGen.Generation}, Ind={selectedIndex}, Fitness={targetInd.Fitness:F4}, Rank={targetInd.Rank}");
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.icons_Generic;

        public override Guid ComponentGuid => new Guid("6C20EA8C-6F7A-4F22-B95F-6E6483B7F49C");
    }
}
