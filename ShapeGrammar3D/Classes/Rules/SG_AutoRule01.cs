using Rhino.Geometry;

using ShapeGrammar3D.Classes.Elements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule01 : SG_Rule
    {
        // --- properties ---

        // from parent class
        // public State RuleState;
        // public string Name;

        // from this class
        
        // public int EID { get; set; }
        // public double T { get; set; }
        public List<string> ElemNames { get; set; } = new List<string>();
        private readonly double[] bounds = { 0.2, 0.8 };

        // --- constructors --- 

        public SG_AutoRule01()
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_01";
            RuleMarker = UT.RULE010_MARKER;
        }

        public SG_AutoRule01(List<string> _eNames)
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_01";
            ElemNames = _eNames;
            RuleMarker = UT.RULE010_MARKER;
            // T = _t;

        }

        // --- methods ---
        // methods of parent class
        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule) 
        {
            throw new NotImplementedException();
        }


        // Moved helper out of local function so it doesn't capture a ref parameter
        private void RebuildNodesAndCounts(SG_Shape ss_ref)
        {
            if (ss_ref.Elems == null) ss_ref.Elems = new List<SG_Element>();

            // Collect all nodes referenced by remaining 1D elements (extend if you have 2D/3D elems)
            var nodes = new List<SG_Node>();
            var seen = new HashSet<int>(); // assumes SG_Node has stable ID property

            foreach (var e in ss_ref.Elems)
            {
                if (e is SG_Elem1D e1 && e1.Nodes != null)
                {
                    foreach (var n in e1.Nodes)
                    {
                        if (n == null) continue;

                        // If SG_Node doesn't have ID, replace this logic with ReferenceEquals-based set
                        if (seen.Add(n.ID))
                            nodes.Add(n);
                    }
                }
            }

            ss_ref.Nodes = nodes;
            ss_ref.nodeCount = ss_ref.Nodes.Count;

            // elementCount as "next free id"
            int maxId = -1;
            foreach (var e in ss_ref.Elems)
                if (e != null) maxId = Math.Max(maxId, e.ID);

            ss_ref.elementCount = maxId + 1;
        }

        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            // --- find relevant range in genotype ---
            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE010_MARKER);

            if (sid < 0 || eid <= sid)
                return "AutoRule01 - wrong marker or invalid range";

            int len = eid - sid;

            if (gt.IntGenes == null || gt.DGenes == null)
                return "AutoRule01 - null genes";

            if (eid > gt.IntGenes.Count)
                return $"AutoRule01 - IntGenes too short ({gt.IntGenes.Count} < {eid})";

            if (eid > gt.DGenes.Count)
                return $"AutoRule01 - DGenes too short ({gt.DGenes.Count} < {eid})";

            if (ss_ref?.Elems == null || ss_ref.Elems.Count == 0)
                return "AutoRule01 - shape has no elements";

            var intGenes = gt.IntGenes.GetRange(sid, len);
            var dGenes = gt.DGenes.GetRange(sid, len);

            // Snapshot original elements for stable indexing
            var originalElems = ss_ref.Elems.ToList();
            int n = Math.Min(originalElems.Count, len);

            // 1) FILTER: keep only elements where gene != 0
            var kept = new List<SG_Element>();
            int selected = 0;

            for (int i = 0; i < n; i++)
            {
                if (intGenes[i] != 0)
                {
                    kept.Add(originalElems[i]);
                    selected++;
                }
            }

            // If nothing selected, do nothing (or clear all if you prefer)
            if (selected == 0)
                return $"AutoRule01: selected=0, elems={ss_ref.Elems.Count} (no change)";

            ss_ref.Elems = kept;

            // IMPORTANT: rebuild Nodes + counters after filtering
            RebuildNodesAndCounts(ss_ref);

            // 2) OPTIONAL SPLIT: try to split the selected original elements (stable index mapping)
            var toRemove = new HashSet<int>();
            var toAdd = new List<SG_Element>();

            int splitOk = 0, skippedTooShort = 0, skippedNon1D = 0;

            for (int i = 0; i < n; i++)
            {
                if (intGenes[i] == 0) continue;

                var elem = originalElems[i] as SG_Elem1D;
                if (elem == null || elem.Ln == null || elem.Nodes == null || elem.Nodes.Length < 2)
                {
                    skippedNon1D++;
                    continue;
                }

                double param = dGenes[i];

                // clamp to your design bounds
                if (param < bounds[0]) param = bounds[0];
                else if (param > bounds[1]) param = bounds[1];

                // Feasibility check: both segments must be >= MIN_SEG_LEN
                double L = elem.Ln.Length;
                double seglen1 = L * param;
                double seglen2 = L * (1.0 - param);

                if (seglen1 < UT.MIN_SEG_LEN || seglen2 < UT.MIN_SEG_LEN)
                {
                    skippedTooShort++;
                    continue;
                }

                // mid node: use current nodeCount as next id
                var midNode = SG_Node.CreateNode(elem, param, ss_ref.nodeCount);
                ss_ref.Nodes.Add(midNode);
                ss_ref.nodeCount++;

                // new elements: use current elementCount as next id
                var newLn0 = new SG_Elem1D(new[] { elem.Nodes[0], midNode }, ss_ref.elementCount, elem.Name) { Autorule = 1 };
                var newLn1 = new SG_Elem1D(new[] { midNode, elem.Nodes[1] }, ss_ref.elementCount + 1, elem.Name) { Autorule = 1 };
                ss_ref.elementCount += 2;

                toRemove.Add(elem.ID);
                toAdd.Add(newLn0);
                toAdd.Add(newLn1);

                splitOk++;
            }

            // Apply split results only within the filtered set
            if (toRemove.Count > 0)
                ss_ref.Elems = ss_ref.Elems.Where(e => !toRemove.Contains(e.ID)).ToList();

            if (toAdd.Count > 0)
                ss_ref.Elems.AddRange(toAdd);

            // Rebuild again to keep Nodes/nodeCount consistent after splits
            RebuildNodesAndCounts(ss_ref);

            return $"AutoRule01: genes={len}, selected={selected}, splitOk={splitOk}, " +
                   $"skippedTooShort={skippedTooShort}, skippedNon1D={skippedNon1D}, " +
                   $"elems={ss_ref.Elems.Count}, nodes={ss_ref.Nodes.Count}";
        }

        /*
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt) 
        {
            // find relevant range in genotype
            int sid = -999;
            int eid = -999;
            List<int> selectedIntGenes;
            List<double> selectedDGenes;

            gt.FindRange(ref sid, ref eid, UT.RULE010_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "Autorule01 - wrong marker";
            }

            // extract relevant genes
            selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            List<int> removeIds = new List<int>();
            for (int i = 0; i < selectedIntGenes.Count; i++)
            {
                if (selectedIntGenes[i] == 0) continue;
                if (i >= ss_ref.Elems.Count) break;

                SG_Elem1D elem = ss_ref.Elems[i] as SG_Elem1D;
                double param = selectedDGenes[i];
                if (param < bounds[0])
                {
                    param = bounds[0];
                }
                else if (param > bounds[1])
                {
                    param = bounds[1];
                }

                double seglen1 = elem.Ln.Length * param;
                double seglen2 = elem.Ln.Length * (1 - param);

                if (seglen1 < UT.MIN_SEG_LEN || seglen2 < UT.MIN_SEG_LEN)
                {
                    ss_ref.Elems = ss_ref.Elems.Where(e => removeIds.Contains(e.ID) == false).ToList();
                    return "Segments are too short for Autorule01.";
                }

                // add intermediate node
                SG_Node midNode = SG_Node.CreateNode(elem, param, ss_ref.nodeCount);
                ss_ref.Nodes.Add(midNode);
                ss_ref.nodeCount++;

                // create 2x Element
                SG_Elem1D newLn0 = new SG_Elem1D(new SG_Node[] { elem.Nodes[0], midNode }, ss_ref.elementCount, elem.Name) { Autorule = 1};
                SG_Elem1D newLn1 = new SG_Elem1D(new SG_Node[] { midNode, elem.Nodes[1] }, ss_ref.elementCount+1, elem.Name) { Autorule = 1 };

                ss_ref.elementCount += 2;

                // remove Element just split
                removeIds.Add(elem.ID);
                ss_ref.Elems.AddRange(new List<SG_Element>() { newLn0, newLn1 });
            }

            ss_ref.Elems = ss_ref.Elems.Where(e => removeIds.Contains(e.ID) == false).ToList();
            int nonZero = selectedIntGenes.Count(g => g != 0);
            return $"AutoRule01: genes={selectedIntGenes.Count}, nonZero={nonZero}, elems={ss_ref.Elems.Count}";

            //return "Auto-rule 01 successfully applied.";

        }
        
        
        */

        public override State GetNextState() 
        {
            throw new NotImplementedException();
        }

        // methods of this class

    }
}
