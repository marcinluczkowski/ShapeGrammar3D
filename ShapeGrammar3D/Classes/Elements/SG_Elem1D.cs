using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.ApplicationSettings;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Classes.Elements
{
    [Serializable]
    public class SG_Elem1D : SG_Element
    {
        // -- properties

        // Properties from SH_Element class
        // public int? ID { get; set; }
        // public string elementName { get; set; }
        // public SH_Node[] Nodes { get; set; } 

        public Line Ln { get; set; }
        public Curve Crv { get; set; }
        public Curve Init_Crv { get; set; }
        public Plane EPln { get; set; }
        public SH_CrossSection_Beam CrossSection { get; set; }

        // --- constructors ---
        public SG_Elem1D()
        {

        }
        public SG_Elem1D(SG_Node[] _nodes, int _id)
        {

            ID = _id;
            Nodes = _nodes;
            CreateLine();
            RegisterElemPln();
        }
        public SG_Elem1D(SG_Node[] _nodes, int _id, string _el_name)
        {
            ID = _id;
            Nodes = _nodes;
            Name = _el_name;
            
            CreateLine();
            RegisterElemPln();

        }

        public SG_Elem1D(Line _ln, int _id, string _el_name, SH_CrossSection_Beam _cs)
        {
            ID = _id;
            Name = _el_name;
            Ln = _ln;
            CrossSection = _cs;
            Init_Crv = _ln.ToNurbsCurve().DuplicateCurve(); //UT.DeepCopy<Curve>(_ln.ToNurbsCurve());
            Crv = _ln.ToNurbsCurve();

            SG_Node[] nodes = new SG_Node[2];
            nodes[0] = new SG_Node(Ln.From, -999);
            nodes[1] = new SG_Node(Ln.To, -999);

            Nodes = nodes;
            RegisterElemPln();


        }

        //public SG_Elem1D(SG_Node[] _nodes, Curve _crv, int _id, string _el_name, SH_CrossSection_Beam _cs)
        //{
        //    ID = _id;
        //    Name = _el_name;
        //    Crv = _crv;
        //    Init_Crv = UT.DeepCopy<Curve>(_crv);

        //    Ln = new Line(_crv.PointAtStart, _crv.PointAtEnd);
        //    CrossSection = _cs;

        //    SG_Node[] nodes = new SG_Node[2];

        //    var node0 = _nodes[0]; // new SG_Node(Ln.From, -999); //250904
        //    var pln = new Plane();
        //    _crv.FrameAt(_crv.Domain.Min, out pln);
        //    node0.NPln = pln;

        //    var node1 = _nodes[1]; // new SG_Node(Ln.To, -999); //250904
        //    var pln2 = new Plane();
        //    _crv.FrameAt(_crv.Domain.Max, out pln2);
        //    node1.NPln = pln2;


        //    // nodes[0] = node0;
        //    // nodes[1] = node1;

        //    Nodes = _nodes;
        //    RegisterElemPln();

        //}

        public SG_Elem1D(SG_Node[] _nodes, Curve _crv, Curve _ini_crv, int _id, string _el_name, SH_CrossSection_Beam _cs)
        {
            ID = _id;
            Name = _el_name;
            Crv = _crv;
            Init_Crv = _ini_crv;

            Ln = new Line(_crv.PointAtStart, _crv.PointAtEnd);
            CrossSection = _cs;

            SG_Node[] nodes = new SG_Node[2];

            var node0 = _nodes[0]; // new SG_Node(Ln.From, -999); //250904
            var pln = new Plane();
            _crv.FrameAt(_crv.Domain.Min, out pln);
            node0.NPln = pln;

            var node1 = _nodes[1]; // new SG_Node(Ln.To, -999); //250904
            var pln2 = new Plane();
            _crv.FrameAt(_crv.Domain.Max, out pln2);
            node1.NPln = pln2;


            // nodes[0] = node0;
            // nodes[1] = node1;

            Nodes = _nodes;
            RegisterElemPln();

        }

        public SG_Elem1D(Curve _crv, int _id, string _el_name, SH_CrossSection_Beam _cs)
        {
            ID = _id;
            Name = _el_name;
            Crv = _crv;
            Init_Crv = _crv?.DuplicateCurve();

            Ln = new Line(_crv.PointAtStart, _crv.PointAtEnd);
            CrossSection = _cs;

            SG_Node[] nodes = new SG_Node[2];

            var node1 = new SG_Node(Ln.From, -999);
            var pln = new Plane();
            _crv.FrameAt(_crv.Domain.Min, out pln);
            node1.NPln = pln;

            var node2 = new SG_Node(Ln.To, -999);
            var pln2 = new Plane();
            _crv.FrameAt(_crv.Domain.Max, out pln2);
            node2.NPln = pln2;


            nodes[0] = node1;
            nodes[1] = node2;

            Nodes = nodes;
            RegisterElemPln();

        }


        // --- methods ---

        //private void CreateNurbs()
        //{
        //    NurbsCurve = NurbsCurve.Create(false, 1, new Point3d[] { Nodes[0].Position, Nodes[1].Position });
        //} 

        private void RegisterElemPln()
        {
            var spt = Nodes[0].Pt;
            var ept = Nodes[1].Pt;

            Vector3d vx = new Vector3d(ept.X - spt.X, ept.Y - spt.Y, ept.Z - spt.Z);

            Vector3d vy, vz;
            int testint = vx.IsParallelTo(Vector3d.ZAxis);
            if (Math.Abs(vx.IsParallelTo(Vector3d.ZAxis, 0.1)) != 1)
            {
                // not parallel to Z-Axis

                vy = Vector3d.CrossProduct(Vector3d.ZAxis, vx);
                vz = Vector3d.CrossProduct(vx, vy);

            }

            else
            {
                // parallel to Z-Axis

                vy = Vector3d.XAxis;
                vz = Vector3d.YAxis;

            }

            Plane epln = new Plane(spt, vy, vz);
            EPln = epln;

        }

        private void CreateLine()
        {
            Ln = new Line(Nodes[0].Pt, Nodes[1].Pt);
        }

        public override SG_Element DeepClone()
        {
            var clonedNodes = Nodes?.Select(n => n?.DeepClone()).ToArray();
            return new SG_Elem1D
            {
                ID = ID,
                Name = Name,
                Autorule = Autorule,
                Nodes = clonedNodes,
                Ln = Ln,
                Crv = Crv?.DuplicateCurve(),
                Init_Crv = Init_Crv?.DuplicateCurve(),
                EPln = EPln,
                CrossSection = CrossSection // if mutable, add its own clone
            };
        }

    }
}


