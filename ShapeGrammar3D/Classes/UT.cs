using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes.Rules;
using ShapeGrammar3D.Classes.Toolbox;


namespace ShapeGrammar3D.Classes
{
    

    // --- variables ---

    // --- input ---

    // --- solve ---

    // --- output ---


    // --- properties ---

    // --- constructors ---

    // --- methods ---

    public class UT
    {
        public static double PRES = 0.001;

        public static double MIN_SEG_LEN = 1.0;

        public static int RULE_END_MARKER = -999;

        public static int RULE010_MARKER = -10;
        public static int RULE011_MARKER = -11;
        public static int RULE020_MARKER = -20;
        public static int RULE030_MARKER = -30;
        public static int RULE031_MARKER = -31;
        public static int RULE040_MARKER = -40;
        public static int RULE041_MARKER = -41;
        public static int RULE050_MARKER = -50;
        public static int RULE051_MARKER = -51;
        public static int RULE060_MARKER = -60;
        public static int RULE061_MARKER = -61;

        public static string CAT = "StructuralGrammar";
        public static string GR_MAT = "01. Material";
        public static string GR_SEC = "02. Section";
        public static string GR_ELM = "03. Element";
        public static string GR_SUP = "04. Support";
        public static string GR_LD = "05. Load";
        public static string GR_ASSEM = "06. Assembly";
        public static string GR_RLS = "07. Rules";
        public static string GR_INT = "08. Interpreter";
        public static string GR_UTIL = "89. Utilities";
        public static string GR_MISC = "99. Misc";


        public static T DeepCopy<T>(T target)
        {
            // Use System.Text.Json for deep copy instead of BinaryFormatter (obsolete).
            // Note: If types used inside T (for example Rhino.Geometry types) are not
            // JSON-serializable, consider adding custom converters or implementing
            // an explicit cloning method on those types.
            if (target == null) return default(T);

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                IncludeFields = true
            };

            string json = JsonSerializer.Serialize(target, options);
            return JsonSerializer.Deserialize<T>(json, options);
        }
        /// <summary>
        /// Works as a grammar interpreter by applying the list of rules to the shape
        /// </summary>
        /// <param name="rules"></param>
        /// <param name="ss"></param>
        public static SG_Shape ApplyRulesToSimpleShape(List<SG_Rule> rules, SG_Shape ss)
        {
            SG_Shape ssCopy = UT.DeepCopy(ss) ; 
            foreach (SG_Rule rule in rules)
            {
                try
                {
                    string message = rule.RuleOperation(ref ssCopy);
                    //comp.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
                }
                catch // (Exception ex)
                {
                    //comp.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);;
                }

            }
            return ssCopy;
        }


        /// <summary>
        /// Private method for creating the boolean support conditions from the simple shape conditions
        /// </summary>
        /// <param name="condInt"></param>
        /// <returns></returns>
        private static List<bool> CreateBooleanConditions(int condInt)
        {
            List<bool> conditions = new List<bool>() { false, false, false, false, false, false };
            int val = condInt;
            for (int i = 5; i >= 0; i--)
            {
                int rest = (int)Math.Pow(2, i);
                if (val - rest >= 0)
                {
                    conditions[i] = true;
                    val -= rest;
                }
            }
            return conditions;
        }



        public static void TakeRandomItem(List<object> fromList, List<double> weights, Random random, out object item)
        {
            // find the sum of weights
            double sum_of_weights = weights.Sum();
            object el = new object();
            // initiate random
            //var random = new Random();
            double rnd = RandomExtensions.NextDouble(random, 0, sum_of_weights);
            //Console.WriteLine("Random number selected: {0}", rnd);
            for (int i = 0; i < weights.Count; i++)
            {
                if (rnd < weights[i])
                {
                    el = fromList[i];
                    break;
                }
                rnd -= weights[i];
            }
            item = el;
        }

        public static class RandomExtensions
        {
            public static double NextDouble(Random random, double min, double max)
            {
                return random.NextDouble() * (max - min) + min;
            }
        }
    }
}
