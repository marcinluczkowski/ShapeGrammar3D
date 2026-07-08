using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class GrammarInterpreter_JsonReader : GH_Component
    {
        public GrammarInterpreter_JsonReader()
          : base("GI_JsonReader", "GI_JsonRead",
              "Reads a GA run JSON file and reconstructs SG_Shape assemblies per generation",
              UT.CAT, UT.GR_INT)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("JSON Path", "JSON", "Path to GA run JSON file", GH_ParamAccess.item);           // 0
            pManager.AddGenericParameter("SG_Shape", "SG_Shape", "Initial SG Assembly (same as used in GA run)", GH_ParamAccess.item); // 1
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules (same as used in GA run)", GH_ParamAccess.list);       // 2
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Shapes", "Shapes", "Reconstructed SG_Shapes {generation}(individual)", GH_ParamAccess.tree);   // 0
            pManager.AddParameter(new Param_TB_Model(), "Models", "Models", "Reconstructed TB_Models {generation}(individual)", GH_ParamAccess.tree); // 1
            pManager.AddNumberParameter("Fitness", "Fitness", "Fitness values from JSON {generation}(individual)", GH_ParamAccess.tree);  // 2
            pManager.AddNumberParameter("Topo", "Topo", "Topology metric from JSON {generation}(individual)", GH_ParamAccess.tree);      // 3
            pManager.AddNumberParameter("Shpe", "Shpe", "Shape metric from JSON {generation}(individual)", GH_ParamAccess.tree);         // 4
            pManager.AddIntegerParameter("ClustGrp", "Clust", "Cluster group from JSON {generation}(individual)", GH_ParamAccess.tree);  // 5
            pManager.AddTextParameter("Info", "Info", "Run metadata and reconstruction summary", GH_ParamAccess.item);                    // 6

            pManager[1].Optional = true;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string jsonPath = string.Empty;
            SG_Shape iniShape = new SG_Shape();
            List<SG_Rule> rules = new List<SG_Rule>();

            if (!DA.GetData(0, ref jsonPath)) return;
            if (!DA.GetData(1, ref iniShape)) return;
            if (!DA.GetDataList(2, rules)) return;

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
                DA.SetData(6, "No generations in file.");
                return;
            }

            // --- Reconstruct shapes per generation ---
            GH_Structure<GH_ObjectWrapper> shapesTree = new GH_Structure<GH_ObjectWrapper>();
            GH_Structure<GH_TB_Model> modelsTree = new GH_Structure<GH_TB_Model>();
            GH_Structure<GH_Number> fitnessTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> topoTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> shpeTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Integer> clustTree = new GH_Structure<GH_Integer>();

            int totalIndividuals = 0;
            int totalFailed = 0;

            for (int g = 0; g < store.Generations.Count; g++)
            {
                GenerationRecord genRecord = store.Generations[g];
                GH_Path path = new GH_Path(genRecord.Generation);

                if (genRecord.Individuals == null) continue;

                for (int i = 0; i < genRecord.Individuals.Count; i++)
                {
                    IndividualRecord rec = genRecord.Individuals[i];
                    totalIndividuals++;

                    // Store metrics from JSON
                    fitnessTree.Append(new GH_Number(rec.Fitness), path);
                    topoTree.Append(new GH_Number(rec.Topo), path);
                    shpeTree.Append(new GH_Number(rec.Shpe), path);
                    clustTree.Append(new GH_Integer(rec.ClustGrp), path);

                    // Reconstruct shape from genotype
                    try
                    {
                        SG_Genotype gt = new SG_Genotype(
                            new List<int>(rec.Chromosome),
                            new List<double>(rec.ChromosomeParam));

                        SG_Shape shape = CloneShape(iniShape);

                        for (int j = 0; j < rules.Count; j++)
                        {
                            rules[j].RuleOperation(ref shape, ref gt);
                        }

                        shape.RegisterElemsToNodes();

                        shapesTree.Append(new GH_ObjectWrapper(shape), path);

                        // Also reconstruct model
                        try
                        {
                            TB_Model model = new TB_Model(shape);
                            SolveLS slv = new SolveLS(ref model);
                            modelsTree.Append(new GH_TB_Model(model), path);
                        }
                        catch
                        {
                            modelsTree.Append(null, path);
                        }
                    }
                    catch (Exception ex)
                    {
                        totalFailed++;
                        shapesTree.Append(null, path);
                        modelsTree.Append(null, path);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            string.Format("Gen {0}, Ind {1} reconstruction failed: {2}",
                                genRecord.Generation, i, ex.Message));
                    }
                }
            }

            // --- Info summary ---
            string info = string.Format(
                "Run ID: {0}\n" +
                "Started: {1}\n" +
                "Generations: {2}\n" +
                "Population Size: {3}\n" +
                "Clusters: {4}\n" +
                "Mutation: {5:F2}  Crossover: {6:F2}  Elite: {7:F2}\n" +
                "Total individuals: {8}\n" +
                "Reconstruction failures: {9}",
                store.RunId,
                store.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                store.Generations.Count,
                store.PopulationSize,
                store.NumClusters,
                store.MutationProb, store.CrossoverProb, store.EliteProb,
                totalIndividuals,
                totalFailed);

            DA.SetDataTree(0, shapesTree);
            DA.SetDataTree(1, modelsTree);
            DA.SetDataTree(2, fitnessTree);
            DA.SetDataTree(3, topoTree);
            DA.SetDataTree(4, shpeTree);
            DA.SetDataTree(5, clustTree);
            DA.SetData(6, info);
        }

        private static SG_Shape CloneShape(SG_Shape source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return source.DeepCopy();
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Properties.Resources.icons_CAT_DataPreview; }
        }
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;


        public override Guid ComponentGuid
        {
            get { return new Guid("C5E7F9A2-3B4D-4E6F-A0B2-9C8D7E6F5A4B"); }
        }
    }
}