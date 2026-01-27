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

            List<int> chromosome = new List<int>();
            List<double> chromosome_param = new List<double>();

            List<SG_Genotype> genotypes = new List<SG_Genotype>();  

            // --- input ---
            if (!DA.GetDataList(0, rls)) return;
            if (!DA.GetData(1, ref num_of_individuals)) return;
            if (!DA.GetDataList(2, max_number_of_rules)) return;

            // --- solve ---

            for (int k = 0; k < num_of_individuals; k++)
            {

                for (int i = 0; i < rls.Count; i++)
                {
                    int len_l = max_number_of_rules[i];
                    SG_Rule rule = rls[i];
                    int rule_marker = rule.RuleMarker;

                    chromosome.Add(rule_marker);
                    chromosome_param.Add(rule_marker);

                    for (int j = 0; j < max_number_of_rules[i]; j++)
                    {
                        var rnd = new Random();
                        var rnd_double = new Random();
                        int random_value = rnd.Next(0, 2); // integer random value
                        double double_value = rnd_double.NextDouble(); // double random value

                        chromosome.Add(random_value);
                        chromosome_param.Add(double_value);

                    }

                    chromosome.Add(-1); // end of rule marker
                    chromosome_param.Add(-1); // end of rule marker

                }

                SG_Genotype gt = new SG_Genotype(chromosome, chromosome_param);
                genotypes.Add(gt);

            }
            // --- output ---
            DA.SetDataList(0, genotypes);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null; //return Properties.Resources.icons_Generic;
            }
        }
        public override Guid ComponentGuid
        {
            get { return new Guid("E313D14B-0A7F-4A3B-BD3B-28EC4760CEA9"); }
        }
    }
}
