using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using ShapeGrammar3D.Classes;
using System.Linq;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Components
{
    // This assembly component is not yet compatibel with other geometries than lines as for the simple bridge and truss roof grammar.
    [Serializable]
    public class Assembly : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Assembly class.
        /// </summary>
        public Assembly()
          : base("Assembly", "Assembly",
              "Assembly",
              UT.CAT, UT.GR_ASSEM)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_Elements", "elems", "SH_Elements", GH_ParamAccess.list); // 0
            pManager.AddGenericParameter("SH_Supports", "sup", "SH_Supports", GH_ParamAccess.list); // 1
            pManager.AddGenericParameter("SH_Loads", "loads", "SH_Loads", GH_ParamAccess.list); // 2

            // make the support and load parameters optional
            pManager[1].Optional = true;
            pManager[2].Optional = true;

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SH_SimpleShape", "SH_SimpleShape", "SH_SimpleShape", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            List<SG_Element> elems = new List<SG_Element>();
            List<SG_Support> sups = new List<SG_Support>();
            List<SG_Load> loads = new List<SG_Load>();
            

            // --- input ---
            if (!DA.GetDataList(0, elems)) return;
           
            if(!DA.GetDataList(1, sups))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "There are no supports in the assembly!");
            }

            if(!DA.GetDataList(2, loads))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "There are no loads in the assembly!");
            }

            // deep copy the input
            elems = UT.DeepCopyList(elems).Cast<SG_Element>().ToList();
            sups  = UT.DeepCopyList(sups);
            loads = UT.DeepCopyList(loads);


            // --- solve ---

            SG_Shape shape = new SG_Shape
            {
                elementCount = 0,
                nodeCount = 0
            };

            List<SG_Node> nodes = new List<SG_Node>();
            List<SG_Element> renumberedElems = new List<SG_Element>();
            List<SG_Support> uniqueSupports = new List<SG_Support>();

            foreach (SG_Element e in elems)
            {
                // element counter
                e.ID = shape.elementCount;
                shape.elementCount++;
                renumberedElems.Add(e);

                // node check and renumbering
                foreach (SG_Node nd in e.Nodes)
                {
                    SG_Node targetNode;

                    // test if there is already a node in this position
                    if (nodes.Any(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001 ))
                    {
                        targetNode = nodes.Where(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001).First();

                        targetNode.Elements.Add(e);
                        continue;
                    }
                    
                    // in case it is a new node
                    nd.ID = shape.nodeCount;
                    nd.Elements.Add(e);
                    nodes.Add(nd);
                    shape.nodeCount++;
                }
            }

            foreach (var sup in sups)
            {
                // find the index of the node where the support applies
                SG_Node nd = nodes.FirstOrDefault( n => n.Pt.DistanceToSquared(sup.Pt) < 0.001 );

                if (nd != null)
                {
                    sup.Node = nd;
                    nd.Support = sup;
                    uniqueSupports.Add(sup); 
                }
            }
            
            // add the loads to the simple shape            
            SortLoads(loads, out List<SG_LineLoad> l_loads, out List<SG_PointLoad> p_loads);

            shape.Nodes = nodes;
            shape.Elems = renumberedElems;
            shape.LineLoads = l_loads;
            shape.PointLoads = p_loads;
            shape.Supports = uniqueSupports;
            shape.SimpleShapeState = State.alpha;

            // --- output ---
            DA.SetData(0, shape); 


        }
        /// <summary>
        /// Private methods for this components
        /// </summary>
        /// 


        private void SortLoads(List<SG_Load> loads ,out List<SG_LineLoad> line_loads, out List<SG_PointLoad> point_loads)
        {
            // create the empty list
            List<SG_PointLoad> pl = new List<SG_PointLoad>();
            List<SG_LineLoad> ll = new List<SG_LineLoad>();

            // iterate through all the loads
            foreach (var l in loads)
            {
                if (l is SG_LineLoad load)
                {
                    ll.Add( load);
                }
                if (l is SG_PointLoad ptload)
                {
                    pl.Add( ptload);
                }
            }
            line_loads = ll;
            point_loads = pl;
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("d3d9eb87-86c0-4891-9d50-6810495145af"); }
        }
    }
}