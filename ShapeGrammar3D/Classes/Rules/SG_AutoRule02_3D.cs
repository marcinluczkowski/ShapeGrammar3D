using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Rhino.Geometry;

using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule02_3D : SG_Rule
    {
        // --- properties ---
        public List<string> ElemNames { get; set; } = new List<string>();
        public double[] Domain { get; set; }
        /// <summary>
        /// Minimum ratio (0.0–1.0) of eligible nodes that must receive struts.
        /// 0 = pure GA control (original behaviour). 0.5 = at least 50% of eligible nodes.
        /// </summary>
        public double MinRatio { get; set; }

        // --- constructors ---
        public SG_AutoRule02_3D()
        {
        }

        public SG_AutoRule02_3D(List<string> _eNames, double[] _domain, double minRatio = 0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule_02_3D";
            ElemNames = _eNames;
            Domain = _domain;
            MinRatio = Math.Clamp(minRatio, 0.0, 1.0);
            RuleMarker = UT.RULE020_MARKER;
        }

        // --- methods ---

        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Nodes;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE020_MARKER);
            if (sid == -999 || eid == -999)
                return "SG_AutoRule_02_3D - wrong marker";

            var selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            var selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);

            double range = Domain[1] - Domain[0];

            ss_ref.UnregisterElemsFromNodes();
            ss_ref.RegisterElemsToNodes();

            SH_CrossSection_Beam def_crosec = ss_ref.Elems?
                .OfType<SG_Elem1D>()
                .FirstOrDefault()?.CrossSection;
            if (def_crosec == null)
            {
                var fallback = new SH_CrossSection_Rectangle(10, 10);
                fallback.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
                def_crosec = fallback;
            }

            int nodeCount = ss_ref.Nodes.Count;
            int geneCount = selectedIntGenes.Count;

            // --- Identify eligible nodes ---
            var eligible = new List<int>();
            for (int i = 0; i < nodeCount; i++)
            {
                var nd = ss_ref.Nodes[i];
                if (nd.Elements.Count == 1
                    && nd.Elements[0].Autorule == UT.RULE020_MARKER)
                    continue;
                if (!nd.Elements.OfType<SG_Elem1D>().Any(e => e.Autorule == UT.RULE010_MARKER))
                    continue;
                eligible.Add(i);
            }

            // --- Determine activation per eligible node (gene wrapping) ---
            var activated = new bool[nodeCount];
            int activeCount = 0;
            foreach (int idx in eligible)
            {
                int geneIdx = idx % geneCount;
                if (selectedIntGenes[geneIdx] == 1)
                {
                    activated[idx] = true;
                    activeCount++;
                }
            }

            // --- Enforce MinRatio ---
            if (MinRatio > 0 && eligible.Count > 0)
            {
                int target = (int)Math.Ceiling(MinRatio * eligible.Count);
                foreach (int idx in eligible)
                {
                    if (activeCount >= target) break;
                    if (!activated[idx])
                    {
                        activated[idx] = true;
                        activeCount++;
                    }
                }
            }

            // --- Create struts at activated nodes ---
            int strutCount = 0;
            string returnMessage = "";

            for (int i = 0; i < nodeCount; i++)
            {
                if (!activated[i]) continue;
                var nd = ss_ref.Nodes[i];

                int geneIdx = i % geneCount;
                double param = Math.Clamp(selectedDGenes[geneIdx], 0.0, 1.0);
                double length = param * range + Domain[0];

                int numStuds = nd.NumStuds;

                if (Math.Abs(length) < UT.MIN_SEG_LEN)
                {
                    returnMessage += $"line too short: at node {nd.ID}\n";
                    continue;
                }

                SG_Elem1D baseElem = nd.Elements
                    .OfType<SG_Elem1D>()
                    .FirstOrDefault(e => e.Autorule == UT.RULE010_MARKER);
                if (baseElem == null) continue;

                Vector3d strutDir = ComputeStrutDirectionFromRule010Beams(nd);

                for (int j = 0; j < numStuds; j++)
                {
                    Line ln = new Line(nd.Pt, strutDir, length);
                    SG_Elem1D elem = new SG_Elem1D(ln, -999, "3DAR2", def_crosec)
                    {
                        Autorule = UT.RULE020_MARKER
                    };

                    elem.Init_Crv = baseElem.Init_Crv;
                    elem.Nodes[0] = nd;
                    elem.Nodes[1] = new SG_Node(ln.To, -999);
                    elem.CrossSection = def_crosec;

                    SG_Elem1D iniElem = nd.Elements.OfType<SG_Elem1D>().FirstOrDefault();
                    if (iniElem != null)
                        elem.Crv = iniElem.Crv;

                    ss_ref.AddNewElement(elem);
                    strutCount++;
                }
            }

            returnMessage += $"SG_AutoRule_02_3D: {strutCount} struts at {activeCount}/{eligible.Count} eligible nodes";
            return returnMessage;
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Strut direction (unit): default along local Z built from all RULE010 beams at
        /// <paramref name="nd"/> — local X = average of outward beam tangents,
        /// local Y = world_Z × local_x, local Z = local_x × local_y.
        /// If local X is parallel to world Z, returns world +Z.
        /// </summary>
        private static Vector3d ComputeStrutDirectionFromRule010Beams(SG_Node nd)
        {
            const double parallelDotTol = 1e-10;
            var zWorld = Vector3d.ZAxis;
            var sum = Vector3d.Zero;

            foreach (var el in nd.Elements.OfType<SG_Elem1D>())
            {
                if (el.Autorule != UT.RULE010_MARKER) continue;
                if (el.Nodes == null || el.Nodes.Length < 2 || el.Nodes[0] == null || el.Nodes[1] == null)
                    continue;

                Vector3d away;
                if (ReferenceEquals(el.Nodes[0], nd))
                    away = el.Nodes[1].Pt - el.Nodes[0].Pt;
                else if (ReferenceEquals(el.Nodes[1], nd))
                    away = el.Nodes[0].Pt - el.Nodes[1].Pt;
                else
                    continue;

                if (away.SquareLength < 1e-24) continue;
                away.Unitize();
                sum += away;
            }

            if (sum.SquareLength < 1e-24)
                return zWorld;

            var localX = sum;
            if (!localX.Unitize())
                return zWorld;

            // local_x parallel to world Z → cross undefined; use world Z as local_z.
            if (Math.Abs(localX * zWorld) >= 1.0 - parallelDotTol)
                return zWorld;

            var localY = Vector3d.CrossProduct(zWorld, localX);
            if (localY.SquareLength < 1e-24 || !localY.Unitize())
                return zWorld;

            var localZ = Vector3d.CrossProduct(localX, localY);
            if (localZ.SquareLength < 1e-24 || !localZ.Unitize())
                return zWorld;

            return localZ;
        }
    }

}
