using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Rule 031: rotates stud/strut elements by a GA-driven angle using a <b>local</b> axis
    /// from the element curve (perpendicular frame at the strut base, Y axis — same idea as Rule 03-3D).
    /// Target elements are filtered by name; empty names default to "3DAR2".
    /// </summary>
    [Serializable]
    public class SG_AutoRule031_3D : SG_Rule
    {
        /// <summary>Element <see cref="SG_Element.Name"/> values to rotate (e.g. 3DAR2). If empty, defaults to name "3DAR2".</summary>
        public List<string> ElemNames { get; set; }

        /// <summary>Rotation domain [min, max] in radians. Angle = DGene * (max - min) + min when the int gene applies.</summary>
        public double[] Domain { get; set; }

        public SG_AutoRule031_3D() { }

        public SG_AutoRule031_3D(List<string> _eNames, double[] _domain)
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_031_3D";
            ElemNames = _eNames;
            Domain = _domain;
            RuleMarker = UT.RULE031_MARKER;
        }

        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }

        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Genotype: marked range <see cref="UT.RULE031_MARKER"/> with paired int/double genes.
        /// Int gene == 0 → default angle (mid-domain); else double gene maps to <see cref="Domain"/>.
        /// </summary>
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            if (Domain == null || Domain.Length < 2)
                return "AutoRule031-3D - invalid Domain";

            int sid = -999;
            int eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE031_MARKER);

            if (sid == -999 || eid == -999)
                return "AutoRule031-3D - wrong marker";

            List<int> selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            List<double> selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            double range = Domain[1] - Domain[0];
            var relevantElems = CollectTargetElements(ss_ref);
            double defaultAngle = (Domain[0] + Domain[1]) / 2.0;
            int geneCount = selectedIntGenes.Count;

            for (int i = 0; i < relevantElems.Count; i++)
            {
                double rotationAngle;
                if (i >= geneCount || selectedIntGenes[i % geneCount] == 0)
                    rotationAngle = defaultAngle;
                else
                    rotationAngle = selectedDGenes[i % geneCount] * range + Domain[0];

                var elem = relevantElems[i] as SG_Elem1D;
                if (elem == null)
                    continue;

                Plane epln = elem.EPln;
                Vector3d rotationAxis = LocalRotationAxisFromCurve(elem);
                epln.Rotate(rotationAngle, rotationAxis);
                elem.EPln = epln;

                elem.Ln = new Line(epln.Origin, epln.ZAxis, elem.Ln.Length);

                if (elem.Nodes != null && elem.Nodes.Length > 1 && elem.Nodes[1] != null)
                    elem.Nodes[1].Pt = elem.Ln.To;
            }

            return $"Auto-rule 031-3D: {relevantElems.Count} elems rotated (local curve-frame Y axis, default={defaultAngle:F3} rad)";
        }

        /// <summary>Same axis choice as <see cref="SG_AutoRule030_3D"/>: perpendicular frame at line start on <see cref="SG_Elem1D.Crv"/>, Y axis.</summary>
        private static Vector3d LocalRotationAxisFromCurve(SG_Elem1D elem)
        {
            Plane epln = elem.EPln;
            if (elem.Crv == null)
                return epln.YAxis;

            Point3d startPt = elem.Ln.From;
            elem.Crv.ClosestPoint(startPt, out double t);
            if (!elem.Crv.PerpendicularFrameAt(t, out Plane targetPln))
                targetPln = epln;

            Vector3d axis = targetPln.YAxis;
            if (!axis.Unitize())
            {
                axis = epln.YAxis;
                if (!axis.Unitize())
                    axis = Vector3d.YAxis;
            }
            return axis;
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }

        private List<SG_Element> CollectTargetElements(SG_Shape ss_ref)
        {
            if (ss_ref?.Elems == null)
                return new List<SG_Element>();

            var names = ElemNames;
            if (names == null || names.Count == 0)
                return ss_ref.Elems.Where(e => e != null && e.Name == "3DAR2").ToList();

            var set = new HashSet<string>(names.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0)
                return ss_ref.Elems.Where(e => e != null && e.Name == "3DAR2").ToList();

            return ss_ref.Elems.Where(e => e != null && e.Name != null && set.Contains(e.Name)).ToList();
        }
    }
}
