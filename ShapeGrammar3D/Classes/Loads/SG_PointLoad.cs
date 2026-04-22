using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class SG_PointLoad : SG_Load
    {
        // --- properties ---
        public Vector3d Forces { get; set; }
        public Vector3d Moments { get; set; }
        public Point3d Position { get; set; }
        // --- constructors --
        public SG_PointLoad()
        {
            // empty
        }
        public SG_PointLoad(Vector3d _forces, Vector3d _moments, Point3d _position)
        {
            Forces = _forces;
            Moments = _moments;
            Position = _position;
        }
        // --- methods ---
        public override SG_Load DeepClone()
        {
            return new SG_PointLoad
            {
                Forces = Forces,
                Moments = Moments,
                Position = Position
            };
        }
    }
}
