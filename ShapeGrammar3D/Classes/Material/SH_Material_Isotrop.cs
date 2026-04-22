using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class SH_Material_Isotrop : SH_Material
    {
        
        // --- properties ---
        
        
        public double E { get; private set; }
        public double G_ip { get; private set; }
        public double G_tr { get; private set; }
        public double Fy { get; private set; }
        public double alphaT { get; set; }
        public double Poisson { get; private set; }

        // --- constructors --
        public SH_Material_Isotrop()
        {

        }
        public SH_Material_Isotrop(string _family, string _name, double _e, double _v, double _fy, double _rho, double _alphaT)
        {
            Family = _family;
            Name = _name;
            Density = _rho;
            E = _e;
            Poisson = _v;
            Fy = _fy;
            G_ip = G_tr = _e / (2 * (1 + _v) );
            alphaT = _alphaT;

        }

        public static SH_Material_Isotrop Default_Material()
        {
            SH_Material_Isotrop mat = new SH_Material_Isotrop();

            mat.Family = "steel";
            mat.Name = "S355";
            mat.Density = 78.00;
            mat.E = 210000;
            mat.Poisson = 0.3;
            mat.Fy = 355;
            mat.G_ip = mat.E / (2 * (1 + mat.Poisson));
            mat.G_tr = mat.E / (2 * (1 + mat.Poisson));
            mat.alphaT = 0.0001;

            return mat;
        }


        // --- methods ---
    }
}
