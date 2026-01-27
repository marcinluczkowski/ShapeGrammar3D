using System.Text.Json;

namespace ShapeGrammar3D.Classes.Toolbox
{
    public static class Common
    {
        // default class
        // --- field ---
        // --- constructors --- 
        // --- methods ---

        // default component
        // --- variables ---
        // --- input --- 
        // --- solve ---
        // --- output ---

        public static double PRES = 0.001;
        public static int BBOX_SEGMENT = 100;
        public static double DIV_DIST_ALONG_AXIS = 1.0;
        public static int DIV_CIRCLE = 18;
        public static double GRAVITY = 9.81; // m/s2

        public static readonly string category = "StructuralGrammar";
        public static readonly string sub_mat = "01. Material";
        public static readonly string sub_sec = "02. Section";
        public static readonly string sub_sup = "04. Support";
        public static readonly string sub_load = "05. Load";
        public static readonly string sub_elem = "03. Element";
        public static readonly string sub_assem = "06. Assembly";
        public static readonly string sub_analize = "07.Analysis";
        public static readonly string sub_post = "09. Post";
        public static readonly string sub_param = "00. Param";
        public static readonly string sub_info = "99. Info";

        public static T DeepCopy<T>(T target)
        {
            // Use System.Text.Json for deep copy
            var json = JsonSerializer.Serialize(target);
            return JsonSerializer.Deserialize<T>(json);
        }


    }
}
