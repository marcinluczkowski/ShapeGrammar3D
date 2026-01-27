using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;
using ShapeGrammar3D.Classes; // Add this if SG_Node is in this namespace or adjust as needed

namespace ShapeGrammar3D.Classes.Elements
{
    [Serializable] 
    public abstract class SG_Element //: SH_CrossSection_Beam
    {
        // --- properties ---
        
        public int ID { get; set; }
        public string Name { get; set; }

        public int Autorule { get; set; }

        public ShapeGrammar3D.Classes.SG_Node[] Nodes { get; set; }

    }
}
