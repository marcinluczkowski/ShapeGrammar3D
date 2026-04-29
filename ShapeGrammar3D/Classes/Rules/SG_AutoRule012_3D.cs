using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule012_3D : SG_Rule
    {
        public List<string> ElemNames { get; set; } = new List<string>();
        public double[] Domain { get; set; }

        public SG_AutoRule012_3D()
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_012_3D";
            RuleMarker = UT.RULE012_MARKER;
            Domain = new[] { -1.0, 1.0 };
        }

        public SG_AutoRule012_3D(List<string> eNames, double[] domain)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_012_3D";
            RuleMarker = UT.RULE012_MARKER;
            ElemNames = eNames ?? new List<string>();
            Domain = (domain != null && domain.Length >= 2) ? domain : new[] { -1.0, 1.0 };
        }

        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Nodes;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }

        public override SG_Rule CopyRule(SG_Rule rule)
        {
            var src = rule as SG_AutoRule012_3D;
            if (src == null) return this;
            return new SG_AutoRule012_3D(
                src.ElemNames != null ? new List<string>(src.ElemNames) : new List<string>(),
                src.Domain != null ? (double[])src.Domain.Clone() : new[] { -1.0, 1.0 });
        }

        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE012_MARKER);
            if (sid == -999 || eid == -999)
                return "SG_AutoRule_012_3D - wrong marker";

            var selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            var selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);
            if (selectedIntGenes.Count == 0 || selectedDGenes.Count == 0)
                return "SG_AutoRule_012_3D - no genes";

            double d0 = Domain != null && Domain.Length > 0 ? Domain[0] : -1.0;
            double d1 = Domain != null && Domain.Length > 1 ? Domain[1] : 1.0;
            if (d1 < d0)
            {
                double tmp = d0;
                d0 = d1;
                d1 = tmp;
            }
            double range = d1 - d0;

            // Refresh node->element registration. Rule01_3D adds new mid-nodes and
            // sub-segment elements without touching SG_Node.Elements lists, so
            // without this refresh the new mid-nodes have an empty Elements list
            // (filter below would always discard them) and the original endpoint
            // nodes still point to the now-removed RULE_INITSHAPE elements.
            ss_ref.UnregisterElemsFromNodes();
            ss_ref.RegisterElemsToNodes();

            // Mid-nodes created by Rule01_3D split: connected to exactly two
            // RULE010-marked sub-segments. Boundary corners (intersection of
            // multiple init-shape lines) typically have >2 connections and are
            // therefore correctly excluded as supports we don't want to move.
            var targetNodes = ss_ref.Nodes
                .Where(n => n != null
                    && n.Elements != null
                    && n.Elements.Count == 2
                    && n.Elements.All(e => e != null && e.Autorule == UT.RULE010_MARKER))
                .ToList();

            if (targetNodes.Count == 0)
                return $"Auto-rule 012-3D: no Rule01-generated mid-nodes found (nodes={ss_ref.Nodes?.Count ?? 0}, elems={ss_ref.Elems?.Count ?? 0}). Make sure Rule01-3D runs before Rule012-3D and produces splits.";

            int movedCount = 0;
            int geneCount = selectedIntGenes.Count;

            for (int i = 0; i < targetNodes.Count; i++)
            {
                int geneIdx = i % geneCount;
                if (selectedIntGenes[geneIdx] == 0) continue;

                double dz = selectedDGenes[geneIdx] * range + d0;
                if (Math.Abs(dz) < 1e-9) continue;

                var nd = targetNodes[i];
                nd.Pt = new Point3d(nd.Pt.X, nd.Pt.Y, nd.Pt.Z + dz);
                if (nd.Support != null)
                    nd.Support.Pt = nd.Pt;

                foreach (var e in nd.Elements.OfType<SG_Elem1D>())
                {
                    if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null)
                        continue;
                    e.Ln = new Line(e.Nodes[0].Pt, e.Nodes[1].Pt);
                    e.Crv = e.Ln.ToNurbsCurve();
                }

                movedCount++;
            }

            return $"Auto-rule 012-3D: moved {movedCount}/{targetNodes.Count} Rule01 mid-nodes in Z (domain=[{d0:F2},{d1:F2}], genes={geneCount})";
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
    }
}
