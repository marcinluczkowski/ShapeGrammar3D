using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;
namespace ShapeGrammar3D.Classes.Elements
{
    [Serializable]
    public class SG_Surface : SG_Element
    {
        // -- properties -- 
        public Surface elementSurface { get; set; }

        // -- constructors --
        public SG_Surface()
        {
            // empty constructor
        }

        public SG_Surface(Surface elementSurface)
        {
            this.elementSurface = elementSurface;
        }

        public SG_Surface(Surface elementSurface, string _name)
        {
            this.elementSurface = elementSurface;
            Name = _name;
        }

        public SG_Surface(string _name, int _id, Surface _surface)
        {
            Name = _name;
            ID = _id;
            elementSurface = _surface;
        }
        
        // -- methods --
    }
}
