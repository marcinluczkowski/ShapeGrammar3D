using System;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    class SH_CrossSection_RHS : SH_CrossSection_Beam
    {
        public double Height { get; private set; }
        public double Width { get; private set; }
        public double Tw { get; private set; }
        public double Tf { get; private set; }

        public SH_CrossSection_RHS() { }

        /// <param name="_name">Section name / tag</param>
        /// <param name="_height">Outer height H [mm]</param>
        /// <param name="_width">Outer width B [mm]</param>
        /// <param name="_tw">Web thickness [mm]</param>
        /// <param name="_tf">Flange thickness [mm]</param>
        public SH_CrossSection_RHS(string _name, double _height, double _width, double _tw, double _tf)
        {
            Name = _name;
            Height = _height;
            Width = _width;
            Tw = _tw;
            Tf = _tf;

            double bi = _width - 2.0 * _tw;
            double hi = _height - 2.0 * _tf;

            Area = _width * _height - bi * hi;
            Iy = (_width * Math.Pow(_height, 3) - bi * Math.Pow(hi, 3)) / 12.0;
            Wy = Iy / (0.5 * _height);
        }

        public SH_CrossSection_RHS(double _height = 100, double _width = 60, double _tw = 5, double _tf = 5)
            : this("default RHS", _height, _width, _tw, _tf) { }

        public double GetCrossSectionWeight()
        {
            return Area * Material.Density;
        }
    }
}
