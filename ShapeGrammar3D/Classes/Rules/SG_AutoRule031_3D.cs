using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Automatic rule 03 for 3D elements.
    /// This rule rotates selected 1D elements (named "3DAR2") around a local axis
    /// determined from the element curve frame and updates the element plane, line and node.
    /// </summary>
    [Serializable]
    public class SG_AutoRule031_3D : SG_Rule
    {
        // --- properties ---

        /// <summary>
        /// Optional list of element name filters (not used directly in current implementation).
        /// </summary>
        public List<string> ElemNames { get; set; }

        /// <summary>
        /// Rotation domain: [min, max]. Rotation angle is computed as DGene * (max-min) + min.
        /// </summary>
        public double[] Domain { get; set; }


        // --- constructors ---

        public SG_AutoRule031_3D() { }

        public SG_AutoRule031_3D(List<string> _eNames, double[] _domain)
        {
            RuleState = State.alpha;
            Name = "SH_AutoRule_031_3D";
            ElemNames = _eNames;
            Domain = _domain;
            RuleMarker = UT.RULE031_MARKER;
        }


    // --- methods ---

        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }

        public override SG_Rule CopyRule(SG_Rule rule)
        {
            // Not implemented: keep behavior consistent with existing design.
            throw new NotImplementedException();
        }

        /// <summary>
        /// Apply the automatic rotation rule to the shape based on the genotype.
        /// The genotype contains a marked range (UT.RULE031_MARKER) with paired int/double genes.
        /// Int gene != 0 => apply rotation for corresponding element index.
        /// Double gene => normalized rotation factor in [0,1] used to compute angle within Domain.
        /// </summary>
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            // Validate domain
            if (Domain == null || Domain.Length < 2)
                return "AutoRule031-3D - invalid Domain";

            // find relevant range in genotype using the rule marker
            int sid = -999;
            int eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE031_MARKER);

            if (sid == -999 || eid == -999)
            {
                return "AutoRule031-3D - wrong marker";
            }

            // extract relevant genes
            // note: GetRange(start, count) expects count = eid - sid (keeps the original behaviour)
            List<int> selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            List<double> selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            double range = Domain[1] - Domain[0];

            // collect elements targeted by this rule (only elements with Name == "3DAR2")
            var relevantElems = ss_ref.Elems.Where(e => e.Name == "3DAR2").ToList();
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

                var iniElem = elem.Nodes[0].Elements
                    .Where(e => e.Autorule == UT.RULE010_MARKER)
                    .OfType<SG_Elem1D>()
                    .FirstOrDefault();

                if (iniElem == null || iniElem.Crv == null)
                    continue;

                // Work with a copy of the element plane
                Plane epln = elem.EPln;

                // find parameter t on element curve closest to the start point of the element line
                Point3d startPt = elem.Ln.From;
                double t;
                elem.Crv.ClosestPoint(startPt, out t);


                // obtain a perpendicular frame (target plane) at parameter t on the curve
                Plane targetPln;
                bool gotFrame = iniElem.Crv.PerpendicularFrameAt(t, out targetPln);
                // bool gotFrame = elem.Crv.PerpendicularFrameAt(t, out targetPln);

                // Fallback: if frame couldn't be computed, use element's existing plane
                if (!gotFrame)
                {
                    targetPln = epln;
                }

                // choose rotation axis: use target frame Z axis (preserves original author's change)
                Vector3d rotationAxis = targetPln.ZAxis;

                // rotate the element plane around the chosen axis by the computed angle (radians)
                epln.Rotate(rotationAngle, rotationAxis);
                elem.EPln = epln;

                // update the element line to originate at the rotated plane origin and follow the rotated Z axis
                // keeping the original element length
                elem.Ln = new Line(epln.Origin, epln.ZAxis, elem.Ln.Length);

                // update the second node position (assumes a two-node element)
                if (elem.Nodes != null && elem.Nodes.Length > 1 && elem.Nodes[1] != null)
                {
                    elem.Nodes[1].Pt = elem.Ln.To;
                }

                // (Optional) keep element name unchanged; original code commented out changing it to "3DAR3"
            }

            return $"Auto-rule 031-3D: {relevantElems.Count} elems rotated (default={defaultAngle:F2} rad)";
        }


        public override State GetNextState()
        {
            // Not implemented in original; preserve existing behavior
            throw new NotImplementedException();
        }
    }
}
