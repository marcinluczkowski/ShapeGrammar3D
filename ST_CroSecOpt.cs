using ShapeGrammar3D.Utilities; // Add this if Common is in this namespace, adjust as needed

namespace StructuralToolBox
{
    public class ST_CroSecOpt : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the _07_CroSecOpt class.
        /// </summary>
        public ST_CroSecOpt()
          : base("Crosec_Opt_EC3", "CrosecOpt",
              "Cross-section optimisation using EC3",
              Common.category, Common.sub_analize) // This line now works if Common is in scope
        {
        }
        // ... rest of the code ...
    }
}