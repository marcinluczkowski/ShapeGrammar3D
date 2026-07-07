using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Rule 032: GA rotation of <b>struts only</b> (see <see cref="SG_Elem1D.StructuralType"/>).
    /// By default the rotation axis is <see cref="Vector3d.CrossProduct"/> of unit strut direction
    /// (element Z) and the initial-curve tangent at the strut foot — i.e. the plane spanned by
    /// strut and host curve, fanning the strut in that plane.
    /// Optional explicit <see cref="RotationPlane"/>: when valid and <see cref="UseGlobalRotationPlane"/> is true,
    /// axis = plane Z (World XY, YZ, etc.).
    /// </summary>
    [Serializable]
    public class SG_AutoRule032_3D : SG_Rule
    {
        public List<string> ElemNames { get; set; }

        public double[] Domain { get; set; }

        /// <summary>When true, rotation axis = normalized <see cref="Plane.ZAxis"/> of <see cref="RotationPlane"/>.</summary>
        public bool UseGlobalRotationPlane { get; set; }

        /// <summary>Global rotation plane (only used when <see cref="UseGlobalRotationPlane"/> is true).</summary>
        public Plane RotationPlane { get; set; }

        public SG_AutoRule032_3D()
        {
            UseGlobalRotationPlane = false;
            RotationPlane = Plane.WorldXY;
        }

        /// <summary>Local axis per strut (strut × init-curve tangent at foot).</summary>
        public SG_AutoRule032_3D(List<string> _eNames, double[] _domain)
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_032_3D";
            ElemNames = _eNames;
            Domain = _domain;
            UseGlobalRotationPlane = false;
            RotationPlane = Plane.WorldXY;
            RuleMarker = UT.RULE032_MARKER;
        }

        /// <summary>Fixed world/plane axis from Grasshopper.</summary>
        public SG_AutoRule032_3D(List<string> _eNames, double[] _domain, Plane rotationPlane)
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_032_3D";
            ElemNames = _eNames;
            Domain = _domain;
            if (rotationPlane.IsValid)
            {
                UseGlobalRotationPlane = true;
                RotationPlane = rotationPlane;
            }
            else
            {
                UseGlobalRotationPlane = false;
                RotationPlane = Plane.WorldXY;
            }
            RuleMarker = UT.RULE032_MARKER;
        }

        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }

        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            if (Domain == null || Domain.Length < 2)
                return "AutoRule032-3D - invalid Domain";

            int sid = -999;
            int eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE032_MARKER);

            if (sid == -999 || eid == -999)
                return "AutoRule032-3D - wrong marker (chromosome has no -32 segment; press Reset on the interpreter to rebuild the population)";

            List<int> selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            List<double> selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            double range = Domain[1] - Domain[0];

            Vector3d globalAxis = Vector3d.ZAxis;
            if (UseGlobalRotationPlane)
            {
                Plane pln = RotationPlane;
                if (!pln.IsValid)
                    pln = Plane.WorldXY;
                globalAxis = pln.ZAxis;
                if (!globalAxis.Unitize())
                    globalAxis = Vector3d.ZAxis;
            }

            var relevantElems = CollectTargetElements(ss_ref, out bool ignoredElemNameFilter);
            if (relevantElems.Count == 0)
                return BuildNoStrutsDiagnostic(ss_ref);

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

                Vector3d rotationAxis = UseGlobalRotationPlane
                    ? globalAxis
                    : RotationAxisFromStrutAndInitCurve(elem);

                Plane epln = elem.EPln;
                epln.Rotate(rotationAngle, rotationAxis);
                elem.EPln = epln;

                elem.Ln = new Line(epln.Origin, epln.ZAxis, elem.Ln.Length);

                if (elem.Nodes != null && elem.Nodes.Length > 1 && elem.Nodes[1] != null)
                    elem.Nodes[1].Pt = elem.Ln.To;
            }

            string mode = UseGlobalRotationPlane
                ? $"global axis≈[{globalAxis.X:F2},{globalAxis.Y:F2},{globalAxis.Z:F2}]"
                : "per-strut axis (strut × init-curve tangent)";
            string nameNote = ignoredElemNameFilter
                ? " [Elem Name list matched no element names — rotated all strut/column-like 1D members instead]"
                : string.Empty;
            return $"Auto-rule 032-3D: {relevantElems.Count} struts rotated ({mode}, default={defaultAngle:F3} rad){nameNote}";
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Struts / columns from Rule02 (-20 or autorule 2), Rule03x rotations, or name-based
        /// detection (3DAR2, AR2, "column", "strut", …). Optional name filter: if the GH list
        /// matches no element names, all candidates are used (same as leaving Elem Name empty).
        /// </summary>
        private List<SG_Element> CollectTargetElements(SG_Shape ss_ref, out bool ignoredElemNameFilter)
        {
            ignoredElemNameFilter = false;
            if (ss_ref?.Elems == null)
                return new List<SG_Element>();

            List<SG_Element> candidates = ss_ref.Elems.Where(e => e is SG_Elem1D e1 && IsStrutOrColumnLike(e1)).ToList();

            var names = ElemNames;
            if (names == null || names.Count == 0)
                return candidates;

            var set = new HashSet<string>(names.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0)
                return candidates;

            var filtered = candidates.Where(e => e.Name != null && set.Contains(e.Name)).ToList();
            if (filtered.Count == 0 && candidates.Count > 0)
            {
                ignoredElemNameFilter = true;
                return candidates;
            }

            return filtered;
        }

        /// <summary>Rule02 family + structural strut + common stud/column names.</summary>
        private static bool IsStrutOrColumnLike(SG_Elem1D e1)
        {
            if (e1.StructuralType == Elem1DStructuralType.Strut) return true;
            if (e1.Autorule == UT.RULE020_MARKER || e1.Autorule == UT.RULE030_MARKER
                || e1.Autorule == UT.RULE031_MARKER || e1.Autorule == UT.RULE032_MARKER
                || e1.Autorule == 2 || e1.Autorule == 3)
                return true;
            string n = e1.Name;
            if (string.IsNullOrEmpty(n)) return false;
            if (n.Equals("column", StringComparison.OrdinalIgnoreCase)
                || n.Equals("strut", StringComparison.OrdinalIgnoreCase)
                || n.Equals("stud", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(n, "3DAR2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "AR2", StringComparison.OrdinalIgnoreCase))
                return true;
            if (n.IndexOf("AR2", StringComparison.OrdinalIgnoreCase) >= 0
                && n.IndexOf("AR4", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("AR5", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("AR6", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
            return false;
        }

        private static string BuildNoStrutsDiagnostic(SG_Shape ss)
        {
            int total = ss?.Elems?.Count ?? 0;
            int n1d = ss?.Elems?.OfType<SG_Elem1D>().Count() ?? 0;
            int n020 = ss?.Elems?.OfType<SG_Elem1D>().Count(e => e.Autorule == UT.RULE020_MARKER || e.Autorule == 2) ?? 0;
            int n010 = ss?.Elems?.OfType<SG_Elem1D>().Count(e => e.Autorule == UT.RULE010_MARKER) ?? 0;
            return "AutoRule032-3D - 0 struts/columns matched. "
                + $"1D elems={n1d}, RULE020(-20)/2={n020}, RULE010(-10) beams={n010}, total elems={total}. "
                + "Put Auto rule 02-3D (and 011 if used) *before* Rule 032 in the merged rule list so struts exist. "
                + "Struts use Name 3DAR2 / AR2 (Rule02). Leave Rule 032 'Elem Name' empty unless names match exactly.";
        }

        /// <summary>Normal to the plane containing strut direction and initial-curve; fallback matches Rule 031 local Y.</summary>
        private static Vector3d RotationAxisFromStrutAndInitCurve(SG_Elem1D elem)
        {
            Vector3d strutDir = elem.EPln.ZAxis;
            if (!strutDir.Unitize())
            {
                if (elem.Ln.Length > 1e-12)
                    strutDir = elem.Ln.UnitTangent;
                else
                    return LocalRotationAxisFallback(elem);
            }

            Curve crv = elem.Init_Crv ?? elem.Crv;
            if (crv == null)
                return LocalRotationAxisFallback(elem);

            Point3d basePt = elem.Ln.From;
            if (!crv.ClosestPoint(basePt, out double t))
                return LocalRotationAxisFallback(elem);

            Vector3d tan = crv.TangentAt(t);
            if (!tan.Unitize())
                return LocalRotationAxisFallback(elem);

            Vector3d axis = Vector3d.CrossProduct(strutDir, tan);
            if (axis.Length < 1e-9)
                return LocalRotationAxisFallback(elem);
            axis.Unitize();
            return axis;
        }

        /// <summary>Same as Rule 031: perpendicular frame at strut foot on <see cref="SG_Elem1D.Crv"/>, Y axis.</summary>
        private static Vector3d LocalRotationAxisFallback(SG_Elem1D elem)
        {
            Plane epln = elem.EPln;
            if (elem.Crv == null)
            {
                var ax = epln.YAxis;
                if (!ax.Unitize()) ax = Vector3d.YAxis;
                return ax;
            }

            Point3d startPt = elem.Ln.From;
            elem.Crv.ClosestPoint(startPt, out double tLocal);
            if (!elem.Crv.PerpendicularFrameAt(tLocal, out Plane targetPln))
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
    }
}
