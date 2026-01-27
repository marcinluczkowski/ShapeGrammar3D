using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShapeGrammar3D.Classes.Elements;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes.Elements
{
    [Serializable]
    public class SG_Solid : SG_Element
    {
        // -- properties --
        public Brep Brep { get; set; }

        // -- constructors --
        public SG_Solid()
        {
            // empty constructor
        }

        public SG_Solid(string _name, int _id)
        {
            Name = _name;
            ID = _id;
        }

        public SG_Solid(string _name, int _id, Brep _brep)
        {
            Name = _name;
            ID = _id;
            Brep = _brep;
        }
        public SG_Solid(string _name, Brep _brep)
        {
            Name = _name;
            Brep = _brep;
        }



        // -- methods


    }
}
