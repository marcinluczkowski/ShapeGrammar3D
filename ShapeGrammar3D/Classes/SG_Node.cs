using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes
{
    [Serializable] 
    public class SG_Node
    {           

        // --- properties ---
        public int ID { get; set;}
        public Point3d Pt { get; set; }
        public SG_Support Support { get; set; }
        public List<SG_Element> Elements { get; set; } = new List<SG_Element>();

        /// 250318
        public Plane NPln { get; set; }

        // 250414
        public int NumStuds { get; set; }    

        // --- constructors --- 
        public SG_Node()
        {
        }
        public SG_Node(Point3d _location, int _id)
        {
            ID = _id;
            Pt = _location;
            NumStuds = 1;

            Support = new SG_Support("000000", Pt)
            {
                Node = this
            };

        }

        // --- methods ---
        /*
        public void Translate(Vector3d vector)
        {
            Position = Position + vector; // translate the point

            // update the corresponding node. 
            Support.Position += vector;

        }*/
        public static SG_Node CreateNode(SG_Element _e, double _t, int _id)
        {
            //var e = (SG_Elem1D)_e;
            //var pt = e.Crv.PointAt(e.Crv.Domain.ParameterAt(_t));

            double mx = (1 - _t) * _e.Nodes[0].Pt.X + _t * _e.Nodes[1].Pt.X;
            double my = (1 - _t) * _e.Nodes[0].Pt.Y + _t * _e.Nodes[1].Pt.Y;
            double mz = (1 - _t) * _e.Nodes[0].Pt.Z + _t * _e.Nodes[1].Pt.Z;
            Point3d newPoint = new Point3d(mx, my, mz);

            SG_Node nd = new SG_Node(newPoint, _id);

            return nd;
        }

        public static SG_Node CreateNodeOnCrv(SG_Element _e, double _t, int _id)
        {
            var e = (SG_Elem1D)_e;
            var pt = e.Crv.PointAt(e.Crv.Domain.ParameterAt(_t));
            var pl = new Plane(); 
            e.Crv.FrameAt(e.Crv.Domain.ParameterAt(_t), out pl);
            
            //double mx = (1 - _t) * _e.Nodes[0].Pt.X + _t * _e.Nodes[1].Pt.X;
            //double my = (1 - _t) * _e.Nodes[0].Pt.Y + _t * _e.Nodes[1].Pt.Y;
            //double mz = (1 - _t) * _e.Nodes[0].Pt.Z + _t * _e.Nodes[1].Pt.Z;
            // Point3d newPoint = new Point3d(pt);

            SG_Node nd = new SG_Node(pt, _id);
            nd.NPln = pl;
            nd.Elements.Add(e);

            return nd;
        }


    }
}
