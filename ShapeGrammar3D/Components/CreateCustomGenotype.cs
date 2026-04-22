using Grasshopper.Kernel;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Rules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Components
{
    public class CreateCustomGenotype: GH_Component
    {
        public CreateCustomGenotype()
          : base("Create Custom Genotype", "CCG",
              "Create a custom genotype from a string input",
              UT.CAT, UT.GR_UTIL)
        {
        }
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Automatic Rules", "Autorules", "Rules for Automatic Interpreter", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Number of individuals", "N", "Number of individuals to create", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Max. length for each rule", "MaxLens", "", GH_ParamAccess.list);

        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Genotypes", "GT", "", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            List<SG_Rule> rls = new List<SG_Rule>();
            int num_of_individuals = 0;
            List<int> max_number_of_rules = new List<int>();

            List<SG_Genotype> genotypes = new List<SG_Genotype>();   

            // --- input ---
            if (!DA.GetDataList(0, rls)) return;
            if (!DA.GetData(1, ref num_of_individuals)) return;
            DA.GetDataList(2, max_number_of_rules);

            // --- solve ---

            var rnd = new Random();
            for (int k = 0; k < num_of_individuals; k++)
            {
                var chromosome = new List<int>();
                var chromosome_param = new List<double>();

                for (int i = 0; i < rls.Count; i++)
                {
                    SG_Rule rule = rls[i];
                    int rule_marker = rule.RuleMarker;
                    int segLen = i < max_number_of_rules.Count
                        ? max_number_of_rules[i]
                        : rule.GetChromosomeLength(new SG_Shape());

                    chromosome.Add(rule_marker);
                    chromosome_param.Add(rule_marker);

                    for (int j = 0; j < segLen; j++)
                    {
                        chromosome.Add(rnd.Next(0, 2));
                        chromosome_param.Add(rnd.NextDouble());
                    }

                    chromosome.Add(UT.RULE_END_MARKER);
                    chromosome_param.Add(UT.RULE_END_MARKER);
                }

                SG_Genotype gt = new SG_Genotype(chromosome, chromosome_param);
                genotypes.Add(gt);
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                string.Format("Created {0} genotypes for {1} rules: [{2}]",
                    num_of_individuals, rls.Count,
                    string.Join(", ", rls.Select(r => r.RuleMarker))));

            // --- output ---
            DA.SetDataList(0, genotypes);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.icons_Generic;
            }
        }
        public override Guid ComponentGuid
        {
            get { return new Guid("E313D14B-0A7F-4A3B-BD3B-28EC4760CEA9"); }
        }
    }
}
