using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class SH_Material
    {
        // --- properties ---
        
        public string Tag { get; internal set; }
        public string Family { get; set; }
        public string Name { get; internal set; }
        public double Density { get; internal set; }
        // --- constructors --
        public SH_Material()
        {

        }
        // --- methods 

    }
}
