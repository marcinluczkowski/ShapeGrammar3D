using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class SH_CrossSection_Beam
    {
        // --- properties ---
        public string Tag { get; set;  }
        public string Name { get; set; }
        public SH_Material Material { get; set; }

        public double Wy { get; internal set; }
        public double Iy { get; internal set; }
        public double Area { get; internal set; }
        // --- constructors --
        public SH_CrossSection_Beam()
        {

        }
        // --- methods ---
    }
}
