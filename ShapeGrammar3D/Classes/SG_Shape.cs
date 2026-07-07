using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;


namespace ShapeGrammar3D.Classes
{
    public enum State { alpha, beta, gamma, delta, epsilon, zeta, eta, theta, end}; // add more if needed. 

    [Serializable]
    public class SG_Shape
    {
        // --- properties ---
        public int nodeCount = 0;
        public int elementCount = 0;
        public List<NurbsCurve> NurbsCurves { get; set; }

        public List<SG_Element> Elems { get; set; } = new List<SG_Element>();

        public List<SG_Node> Nodes { get; set; }
        public List<SG_Support> Supports { get; set; }
        public List<SG_LineLoad> LineLoads { get; set; }
        public List<SG_PointLoad> PointLoads { get; set; }
        public State SimpleShapeState { get; set; }
        public Brep BoundaryBrep { get; set; }
        public Mesh BoundaryMesh { get; set; }
        public double BoundaryViolationRatio { get; set; } = 0.0;
        public double BoundaryViolationWeight { get; set; } = 0.0;

        // --- constructors ---
        public SG_Shape()
        {
            // empty constructor
            
        }

        // --- methods ---
        public void AddLine(SG_Element _line)
        {
            Elems.Add(_line);
        }

        public void AddNewElement(SG_Elem1D _e)
        {
            _e.ID = elementCount;
            elementCount++;
            Elems.Add(_e);

            for (int k = 0; k < _e.Nodes.Length; k++)
            {
                SG_Node nd = _e.Nodes[k];
                SG_Node existing = Nodes.FirstOrDefault(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001);

                if (existing != null)
                {
                    existing.Elements.Add(_e);
                    _e.Nodes[k] = existing;
                    continue;
                }

                nd.ID = nodeCount;
                nd.Elements.Add(_e);
                Nodes.Add(nd);
                nodeCount++;
            }

            if (_e.MidNodes != null)
            {
                for (int k = 0; k < _e.MidNodes.Count; k++)
                {
                    SG_Node nd = _e.MidNodes[k];
                    if (nd == null) continue;

                    SG_Node existing = Nodes.FirstOrDefault(n => n.Pt.DistanceToSquared(nd.Pt) < 0.001);
                    if (existing != null)
                    {
                        if (!existing.Elements.Contains(_e)) existing.Elements.Add(_e);
                        _e.MidNodes[k] = existing;
                        continue;
                    }

                    nd.ID = nodeCount;
                    if (!nd.Elements.Contains(_e)) nd.Elements.Add(_e);
                    Nodes.Add(nd);
                    nodeCount++;
                }
            }
        }

        public void RemoveUnusedNodes()
        {
            // UnregisterElemsFromNodes();
            var newNodes = Nodes.Where(n => n.Elements.Count != 0);
            Nodes = newNodes.ToList();
        }

        /// <summary>
        /// Registers every element in Elems to its endpoint nodes. Ensures all nodes
        /// (including those created by rule 02 or other rules) have their incident
        /// elements listed, so angle/feasibility analysis runs on all nodes.
        /// </summary>
        public void RegisterElemsToNodes()
        {
            if (Elems == null) return;
            foreach (SG_Element e in Elems)
            {
                if (e?.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null)
                    continue;
                if (!e.Nodes[0].Elements.Contains(e)) e.Nodes[0].Elements.Add(e);
                if (!e.Nodes[1].Elements.Contains(e)) e.Nodes[1].Elements.Add(e);

                if (e is SG_Elem1D e1d && e1d.MidNodes != null)
                {
                    foreach (SG_Node mn in e1d.MidNodes)
                    {
                        if (mn == null) continue;
                        if (!mn.Elements.Contains(e)) mn.Elements.Add(e);
                    }
                }
            }
        }

        public void UnregisterElemsFromNodes()
        {
            foreach (SG_Node n in Nodes)
            {
                foreach (SG_Element e in n.Elements.ToList())
                {
                    if (!e.Nodes.Contains(n))
                    {
                        n.Elements.Remove(e); 
                    } 
                }
            }

        }

        public List<Line> GetLinesFromShape()
        {
            return Elems.Select(e => (e as SG_Elem1D).Ln).ToList(); 
        }

        public void TranslateNode(Vector3d vec, int nodeInd)
        {
            SG_Node node = Nodes[nodeInd];
            Point3d newPoint = node.Pt + vec;
            // move the point
            Nodes[nodeInd].Pt = newPoint;
        }

        public SG_Shape DeepCopy()
        {
            var clone = new SG_Shape
            {
                nodeCount = nodeCount,
                elementCount = elementCount,
                SimpleShapeState = SimpleShapeState,
                BoundaryBrep = BoundaryBrep?.DuplicateBrep(),
                BoundaryMesh = BoundaryMesh?.DuplicateMesh(),
                BoundaryViolationRatio = BoundaryViolationRatio,
                BoundaryViolationWeight = BoundaryViolationWeight,
                NurbsCurves = NurbsCurves?.Select(curve => curve?.DuplicateCurve() as NurbsCurve).ToList()
            };

            var nodeMap = new Dictionary<SG_Node, SG_Node>();

            List<SG_Node> clonedNodes = null;
            if (Nodes != null)
            {
                clonedNodes = new List<SG_Node>(Nodes.Count);
                foreach (var node in Nodes)
                {
                    if (node == null)
                    {
                        clonedNodes.Add(null);
                        continue;
                    }

                    var clonedNode = node.DeepClone();
                    nodeMap[node] = clonedNode;
                    clonedNodes.Add(clonedNode);
                }
            }
            clone.Nodes = Nodes != null ? clonedNodes ?? new List<SG_Node>() : null;

            List<SG_Element> clonedElems = null;
            if (Elems != null)
            {
                clonedElems = new List<SG_Element>(Elems.Count);
                foreach (var elem in Elems)
                {
                    if (elem == null)
                    {
                        clonedElems.Add(null);
                        continue;
                    }

                    var elemClone = elem.DeepClone();

                    if (elem.Nodes != null && elemClone.Nodes != null)
                    {
                        for (int i = 0; i < elem.Nodes.Length; i++)
                        {
                            var sourceNode = elem.Nodes[i];
                            if (sourceNode != null && nodeMap.TryGetValue(sourceNode, out var mappedNode))
                            {
                                elemClone.Nodes[i] = mappedNode;
                                mappedNode.Elements.Add(elemClone);
                            }
                            else if (elemClone.Nodes[i] != null)
                            {
                                elemClone.Nodes[i].Elements.Add(elemClone);
                            }
                        }
                    }

                    if (elem is SG_Elem1D srcE1d && elemClone is SG_Elem1D dstE1d)
                    {
                        var srcMids = srcE1d.MidNodes;
                        var dstMids = dstE1d.MidNodes ?? new List<SG_Node>();
                        if (srcMids != null)
                        {
                            for (int i = 0; i < srcMids.Count && i < dstMids.Count; i++)
                            {
                                var src = srcMids[i];
                                if (src != null && nodeMap.TryGetValue(src, out var mapped))
                                {
                                    dstMids[i] = mapped;
                                    if (!mapped.Elements.Contains(elemClone))
                                        mapped.Elements.Add(elemClone);
                                }
                                else if (dstMids[i] != null)
                                {
                                    if (!dstMids[i].Elements.Contains(elemClone))
                                        dstMids[i].Elements.Add(elemClone);
                                }
                            }
                        }
                    }

                    clonedElems.Add(elemClone);
                }
            }
            clone.Elems = Elems != null ? clonedElems ?? new List<SG_Element>() : null;

            List<SG_Support> clonedSupports = null;
            if (Supports != null)
            {
                clonedSupports = new List<SG_Support>(Supports.Count);
                foreach (var support in Supports)
                {
                    if (support == null)
                    {
                        clonedSupports.Add(null);
                        continue;
                    }

                    var supportClone = support.DeepClone();
                    if (support.Node != null && nodeMap.TryGetValue(support.Node, out var mappedNode))
                    {
                        supportClone.Node = mappedNode;
                        mappedNode.Support = supportClone;
                    }

                    clonedSupports.Add(supportClone);
                }
            }
            clone.Supports = Supports != null ? clonedSupports ?? new List<SG_Support>() : null;

            clone.LineLoads = LineLoads != null
                ? LineLoads.Select(ll => ll != null ? (SG_LineLoad)ll.DeepClone() : null).ToList()
                : null;

            clone.PointLoads = PointLoads != null
                ? PointLoads.Select(pl => pl != null ? (SG_PointLoad)pl.DeepClone() : null).ToList()
                : null;

            return clone;
        }
        

    }
}
