using CSparse;
using CSparse.Double;
using CSparse.Double.Factorization;
using CSparse.Storage;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShapeGrammar3D.Classes.Toolbox;

namespace ShapeGrammar3D.Classes.Toolbox
{
    [Serializable]
    public class TB_Model
    {
        // --- field ---
        public List<TB_Element_1D> Elem1Ds { get; private set; } = null;
        public List<TB_Support> Sups { get; private set; } = null;
        public List<TB_Load> Loads { get; private set; } = null;
        public BoundingBox Bbox { get; private set; }
        public List<Node> Nodes { get; private set; }
        public double Weight { get; private set; }
        public List<bool> Validity { get; private set; } = new List<bool>();
        public DenseColumnMajorStorage<double> KG { get; set; } = null;
        public DenseColumnMajorStorage<double> LM { get; set; } = null;
        public List<double[]> Disps { get; private set; } = new List<double[]>();
        public int[] LCs { get; private set; }
        public int? SelectedLC { get; set; } = null;

        // --- constructors --- 
        public TB_Model() { }
        public TB_Model(List<TB_Element_1D> _elem1Ds, List<TB_Support> _sups, List<TB_Load> _loads)
        {
            Elem1Ds = _elem1Ds;
            Sups = _sups;
            Loads = _loads;

            Weight = Calc_Weight();
            
            Bbox = CreateBBox();
            Nodes = CreateNodes_SetElemIds();

            Validity.Add(CheckSupports());
            Validity.Add(CheckLoads());
        }

        public TB_Model(SG_Shape sg_shape)
        {
            Elem1Ds = SG_Elms2TB_Elms(sg_shape.Elems);
            Sups = SG_Sups2TB_Sups(sg_shape.Supports);
            Loads = SG_lds2TB_lds(sg_shape.PointLoads);

            Weight = Calc_Weight();

            Bbox = CreateBBox();
            Nodes = CreateNodes_SetElemIds();

            Validity.Add(CheckSupports());
            Validity.Add(CheckLoads());
        }

        // --- methods --

        private List<TB_Load> SG_lds2TB_lds(List<SG_PointLoad> sg_lds)
        { 
            List<TB_Load> tb_lds = new List<TB_Load>();
            foreach (SG_PointLoad sg_pl in sg_lds)
            {
                TB_Load_Point tb_pl = new TB_Load_Point(sg_pl.Position, sg_pl.Forces, sg_pl.Moments, 0);
                tb_lds.Add(tb_pl);
            }

            return tb_lds;
        }


        private List<TB_Support> SG_Sups2TB_Sups(List<SG_Support> sg_sups)
        {
            List<TB_Support> tb_sups = new List<TB_Support>();
            foreach (SG_Support s in sg_sups)
            {
                var bools = s.GetBoolConditions();
                string txt = "";
                foreach (var b in bools)
                {
                    if (b == true)
                    {
                        txt += "1";
                    }
                    else
                    {
                        txt += "0";
                    }
                }
                TB_Support tb_s = new TB_Support(s.Pt, txt);
                tb_sups.Add(tb_s);
            }
            return tb_sups;
        }
        private List<TB_Element_1D> SG_Elms2TB_Elms(List<SG_Element> sg_elms)
        {
            List<TB_Element_1D> tb_elems = new List<TB_Element_1D>();
            foreach (SG_Element e in sg_elms)
            {
                if (e is SG_Elem1D sg_e1d)
                {
                    var sg_e = e as SG_Elem1D;
                    var sg_cs = sg_e.CrossSection as SH_CrossSection_Rectangle;
                    SH_Material_Isotrop sg_m = sg_e.CrossSection.Material as SH_Material_Isotrop;

                    TB_Material tb_m = new TB_Material(sg_m.Tag, sg_m.E, sg_m.G_ip, sg_m.Density, sg_m.alphaT, sg_m.Fy);

                    TB_Section tb_sec = new Section_Rect(tb_m, sg_cs.Name, sg_cs.width, sg_cs.height);
                    var zvec = sg_e1d.EPln.ZAxis;
                    TB_Element_1D tb_e1d = new TB_Element_1D(sg_e1d.Ln, sg_e1d.Name, tb_sec, zvec, sg_e.Ln.Length);
                    tb_elems.Add(tb_e1d);
                }
            }
            return tb_elems;
        }

        private double Calc_Weight()
        {
            double weight = 0.0;
            
            foreach (TB_Element_1D e in Elem1Ds)
            {
                weight += e.Weight;
            }

            return weight;
        }

        private List<Node> CreateNodes_SetElemIds()
        {
            int cnt_elemid = 0;
            List<Node> nodes = new List<Node>();
            foreach (TB_Element_1D elem in Elem1Ds)
            {
                NodeCheckAndRegister(elem, ref nodes);
                elem.Id = cnt_elemid;
                cnt_elemid++;
            }

            return nodes;
        }

        private void NodeCheckAndRegister(TB_Element_1D _elem, ref List<Node> _nodes)
        {
            _elem.Nodes.Clear();

            List<Point3d> pts = new List<Point3d>(2) 
                                    { _elem.Line.From, _elem.Line.To };

            foreach (Point3d p in pts)
            {
                Node nd = Node.FindNode(p, _nodes, Bbox);

                if (nd == null)
                {
                    nd = new Node(p, _nodes.Count, Bbox);
                    _nodes.Add(nd);

                }

                nd.Elems.Add(_elem);

                _elem.Nodes.Add(nd);
            }

        }

        private BoundingBox CreateBBox()
        {
            BoundingBox bb = new BoundingBox();
            IEnumerable<Line> lns = Elem1Ds.Select(x => x.Line);
            foreach (Line l in lns)
            {
                bb.Union(l.From);
                bb.Union(l.To);
            }

            return bb;
        }

        private bool CheckSupports()
        {
            foreach (TB_Support s in Sups)
            {
                Node nd = Node.FindNode(s.Pt, Nodes, Bbox);

                if (nd == null)
                {
                    return false;
                }

                else
                {
                    s.Node = nd;
                    nd.Sup = s;
                }
            }

            return true;
        }

        private bool CheckLoads()
        {
            if (Loads != null)
            {
                foreach (TB_Load l in Loads)
                {
                    if (!(l is TB_Load_Point pl)) continue;

                    Node nd = Node.FindNode(pl.Pt, Nodes, Bbox);

                    if (nd == null)
                    {
                        return false;
                    }

                    else
                    {
                        pl.Node = nd;
                    }
                }
            
                LCs = Loads.Select(x => x.Lc.Value).Distinct().ToArray();
            }
            return true;
        }

        public TB_Model DeepCopy()
        {
            return (TB_Model)base.MemberwiseClone();
        }

        public override string ToString()
        {
            string txt = "Model, Node: " + Nodes.Count.ToString();
            return txt;
        }
        public bool IsValid()
        {

            return (Elem1Ds != null) && (Sups != null) && (Loads != null);
        }
    }

    public class GH_TB_Model : GH_Goo<TB_Model>
    {
        public GH_TB_Model() { }
        public GH_TB_Model(GH_TB_Model other) : base(other.Value)
        {
            this.Value = other.Value.DeepCopy();
        }
        public GH_TB_Model(TB_Model mdl) : base(mdl)
        {
            this.Value = mdl;
        }
        public override bool IsValid => base.m_value.IsValid();
        public override string TypeName => "Model";
        public override string TypeDescription => "Model";
        public override IGH_Goo Duplicate()
        {
            return new GH_TB_Model(this);
        }
        public override string ToString()
        {
            return Value.ToString();
        }
    }

    public class Param_TB_Model : GH_PersistentParam<GH_TB_Model>
    {
        public Param_TB_Model() : base(
            new GH_InstanceDescription(
                "Model", "Model", "Model 1D", Common.category, Common.sub_param
                )
            )
        { }

        public override Guid ComponentGuid => new Guid("d2f502e9-33f2-45f1-83c1-ae8dd416d679");

        protected override System.Drawing.Bitmap Icon { get {
                return
                    null;// Properties.Resources.icons_P_Mdl; 
            } }  //Set icon image

        protected override GH_GetterResult Prompt_Plural(ref List<GH_TB_Model> values)
        {
            return GH_GetterResult.success;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_TB_Model value)
        {
            return GH_GetterResult.success;
        }


    }


}
