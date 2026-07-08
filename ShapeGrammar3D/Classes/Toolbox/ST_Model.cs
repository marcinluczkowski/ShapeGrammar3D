using CSparse;
using CSparse.Double;
using CSparse.Double.Factorization;
using CSparse.Storage;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
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
            Loads = SG_lds2TB_lds(sg_shape);

            Weight = Calc_Weight();

            Bbox = CreateBBox();
            Nodes = CreateNodes_SetElemIds();

            Validity.Add(CheckSupports());
            Validity.Add(CheckLoads());
        }

        // --- methods --

        /// <summary>
        /// Builds the FEM load list from an SG_Shape:
        ///  - point loads pass straight through;
        ///  - line loads are lumped onto the FEM nodes of every TB element
        ///    that sits on the host SG element, weighting each node by its
        ///    tributary chord length.
        /// </summary>
        private List<TB_Load> SG_lds2TB_lds(SG_Shape sg_shape)
        {
            var tb_lds = new List<TB_Load>();
            if (sg_shape == null) return tb_lds;

            if (sg_shape.PointLoads != null)
            {
                foreach (SG_PointLoad sg_pl in sg_shape.PointLoads)
                {
                    if (sg_pl == null) continue;
                    tb_lds.Add(new TB_Load_Point(sg_pl.Position, sg_pl.Forces, sg_pl.Moments, 0));
                }
            }

            if (sg_shape.LineLoads != null && sg_shape.LineLoads.Count > 0 && Elem1Ds != null)
            {
                LumpLineLoads(sg_shape, tb_lds);
            }

            return tb_lds;
        }

        /// <summary>
        /// Resolve every <see cref="SG_LineLoad"/> against the TB element list
        /// and replace it with equivalent end-node lumped point loads on each
        /// FEM segment that belongs to the addressed SG element.
        /// </summary>
        private void LumpLineLoads(SG_Shape sg_shape, List<TB_Load> tb_lds)
        {
            // Group TB elements by Tag so we can match SG_LineLoad.ElementId to all
            // FEM segments that share the same source SG element name.
            var byTag = new Dictionary<string, List<TB_Element_1D>>(StringComparer.OrdinalIgnoreCase);
            foreach (var te in Elem1Ds)
            {
                if (te?.Tag == null) continue;
                if (!byTag.TryGetValue(te.Tag, out var list))
                {
                    list = new List<TB_Element_1D>();
                    byTag[te.Tag] = list;
                }
                list.Add(te);
            }

            // Map ID-based addressing too (when the SG element had no Name).
            var byId = new Dictionary<int, List<TB_Element_1D>>();
            if (sg_shape.Elems != null)
            {
                for (int i = 0; i < sg_shape.Elems.Count; i++)
                {
                    if (!(sg_shape.Elems[i] is SG_Elem1D sgE)) continue;
                    string tag = sgE.Name;
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (!byTag.TryGetValue(tag, out var list)) continue;
                    byId[sgE.ID] = list;
                }
            }

            foreach (var ll in sg_shape.LineLoads)
            {
                if (ll == null) continue;

                List<TB_Element_1D> targets = null;
                if (!string.IsNullOrEmpty(ll.ElementId))
                {
                    if (byTag.TryGetValue(ll.ElementId, out var byTagHit))
                    {
                        targets = byTagHit;
                    }
                    else if (int.TryParse(ll.ElementId, out int idHit) && byId.TryGetValue(idHit, out var byIdHit))
                    {
                        targets = byIdHit;
                    }
                }

                if (targets == null)
                {
                    // No element id -> apply the line load to every TB element
                    // (matches the spec: "If none are present the load applies to all").
                    targets = Elem1Ds;
                }

                foreach (var seg in targets)
                {
                    double L = seg.Line.Length;
                    if (L < 1e-12) continue;
                    Vector3d half = ll.Load * (L * 0.5);
                    tb_lds.Add(new TB_Load_Point(seg.Line.From, half, Vector3d.Zero, 0));
                    tb_lds.Add(new TB_Load_Point(seg.Line.To,   half, Vector3d.Zero, 0));
                }
            }
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
        /// <summary>
        /// Converts all SG elements (including rule 02 columns) to TB elements.
        /// Elements with <see cref="SG_Elem1D.MidNodes"/> are split chord-wise at
        /// each interior node so the FEM sees real nodes where the user dropped
        /// non-endpoint loads, while the SG_Shape itself stays unsplit.
        /// Uses default material when CrossSection or Material is null so no
        /// elements are dropped.
        /// </summary>
        private List<TB_Element_1D> SG_Elms2TB_Elms(List<SG_Element> sg_elms)
        {
            var tb_elems = new List<TB_Element_1D>();
            if (sg_elms == null) return tb_elems;

            foreach (SG_Element e in sg_elms)
            {
                if (!(e is SG_Elem1D sg_e1d)) continue;

                Point3d fromPt = sg_e1d.Nodes?[0]?.Pt ?? sg_e1d.Ln.From;
                Point3d toPt   = sg_e1d.Nodes?[1]?.Pt ?? sg_e1d.Ln.To;
                Line chord = new Line(fromPt, toPt);

                var sg_m = (sg_e1d.CrossSection?.Material as SH_Material_Isotrop) ?? SH_Material_Isotrop.Default_Material();
                TB_Material tb_m = new TB_Material(sg_m.Tag ?? "default", sg_m.E, sg_m.G_ip, sg_m.Density, sg_m.alphaT, sg_m.Fy);
                Vector3d zvec = chord.Direction;

                TB_Section tb_sec = BuildTBSection(sg_e1d, tb_m);

                // Build the ordered chord parameter list including endpoints + mid-nodes.
                var sortedParams = new List<(double t, Point3d pt)> { (0.0, chord.From) };
                if (sg_e1d.MidNodes != null && sg_e1d.MidNodes.Count > 0)
                {
                    Vector3d ab = chord.To - chord.From;
                    double len2 = ab.SquareLength;
                    foreach (var mn in sg_e1d.MidNodes)
                    {
                        if (mn == null) continue;
                        double t = len2 > 1e-18
                            ? Math.Clamp(((mn.Pt - chord.From) * ab) / len2, 0.0, 1.0)
                            : 0.0;
                        sortedParams.Add((t, mn.Pt));
                    }
                }
                sortedParams.Add((1.0, chord.To));

                sortedParams.Sort((a, b) => a.t.CompareTo(b.t));

                // Drop duplicates that are within tolerance (fixes the case where a
                // mid-node coincides with an endpoint).
                for (int i = sortedParams.Count - 1; i > 0; i--)
                {
                    if (sortedParams[i].pt.DistanceToSquared(sortedParams[i - 1].pt) < 1e-9)
                        sortedParams.RemoveAt(i);
                }

                string tag = sg_e1d.Name; // shared across all sub-segments so line-load lookup works

                for (int i = 0; i < sortedParams.Count - 1; i++)
                {
                    var seg = new Line(sortedParams[i].pt, sortedParams[i + 1].pt);
                    if (seg.Length < 1e-12) continue;
                    tb_elems.Add(new TB_Element_1D(seg, tag, tb_sec, zvec, seg.Length));
                }
            }
            return tb_elems;
        }

        private static TB_Section BuildTBSection(SG_Elem1D sg_e1d, TB_Material tb_m)
        {
            if (sg_e1d.CrossSection is SH_CrossSection_RHS sg_rhs)
                return new Section_RHS(tb_m, sg_rhs.Name, sg_rhs.Height, sg_rhs.Width, sg_rhs.Tw, sg_rhs.Tf);

            if (sg_e1d.CrossSection is SH_CrossSection_Rectangle sg_rect)
                return new Section_Rect(tb_m, sg_rect.Name, sg_rect.width, sg_rect.height);

            if (sg_e1d.CrossSection != null && sg_e1d.CrossSection.Area > 0)
            {
                double dim = Math.Sqrt(sg_e1d.CrossSection.Area);
                return new Section_Rect(tb_m, sg_e1d.CrossSection.Name ?? "default", dim, dim);
            }

            const double defaultDim = 10.0;
            return new Section_Rect(tb_m, "default", defaultDim, defaultDim);
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
            bool allFound = true;
            foreach (TB_Support s in Sups)
            {
                Node nd = Node.FindNode(s.Pt, Nodes, Bbox);

                if (nd == null)
                    nd = Nodes.FirstOrDefault(n => n.Pt.DistanceTo(s.Pt) < Common.PRES);

                if (nd == null)
                {
                    allFound = false;
                    continue;
                }

                s.Node = nd;
                nd.Sup = s;
            }

            return allFound;
        }

        private bool CheckLoads()
        {
            bool allFound = true;
            if (Loads != null)
            {
                double diag = Bbox.IsValid ? Bbox.Diagonal.Length : 1.0;
                // Loose snap: curved SG members vs straight chord FEM, float drift, etc.
                double fuzzyTol = Math.Max(Common.PRES * 50, diag * 1e-5);

                foreach (TB_Load l in Loads)
                {
                    if (!(l is TB_Load_Point pl)) continue;

                    Node nd = Node.FindNode(pl.Pt, Nodes, Bbox);

                    if (nd == null)
                        nd = Nodes.FirstOrDefault(n => n.Pt.DistanceTo(pl.Pt) < Common.PRES);

                    if (nd == null && Nodes != null && Nodes.Count > 0)
                    {
                        Node nearest = null;
                        double best = double.MaxValue;
                        foreach (var n in Nodes)
                        {
                            if (n == null) continue;
                            double d = n.Pt.DistanceTo(pl.Pt);
                            if (d < best) { best = d; nearest = n; }
                        }
                        if (nearest != null && best <= fuzzyTol)
                            nd = nearest;
                    }

                    if (nd == null)
                    {
                        allFound = false;
                        continue;
                    }

                    pl.Node = nd;
                    pl.Pt = nd.Pt;
                }
            
                LCs = Loads.Select(x => x.Lc.Value).Distinct().ToArray();
            }
            return allFound;
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
                "Structural model", "Model",
                "1D finite-element model (members, supports, nodal loads) used by the internal solver and previews.",
                Common.category, Common.sub_param)
            )
        { }

        public override Guid ComponentGuid => new Guid("d2f502e9-33f2-45f1-83c1-ae8dd416d679");

        protected override System.Drawing.Bitmap Icon { get {
                return global::ShapeGrammar3D.Properties.Resources.icons_CAT_Param;
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
