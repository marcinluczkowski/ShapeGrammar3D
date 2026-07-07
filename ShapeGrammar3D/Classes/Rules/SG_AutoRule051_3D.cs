using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SG_AutoRule051_3D : SG_Rule
    {
        // --- properties ---
        public string ElemName { get; set; }
        public int[] Domain { get; set; }
        public double MinRatio { get; set; }

        // --- constructors ---
        public SG_AutoRule051_3D()
        {
        }

        public SG_AutoRule051_3D(string _eName, int[] _domain, double minRatio = 0.0)
        {
            RuleState = State.alpha;
            Name = "SG_AutoRule051-3D";
            ElemName = _eName;
            Domain = _domain;
            MinRatio = Math.Clamp(minRatio, 0.0, 1.0);
            RuleMarker = UT.RULE051_MARKER;
        }

        // --- methods ---
        public override RuleIterationTarget IterationTarget => RuleIterationTarget.Studs;

        public override void NewRuleParameters(Random random, SG_Shape ss) { }
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds braces between strut tips (RULE020) along the shared RULE010 base
        /// subdivision chain.
        ///
        /// Neighbours are discovered by walking the actual graph of RULE010
        /// sub-elements through their shared SG_Nodes — NOT by reference equality
        /// of <c>Joined_Init_Crv</c> (which is broken by <c>SG_Shape.DeepCopy()</c>
        /// when the iniShape contains several beams sharing one joined parent
        /// curve). From each strut foot we walk both incident RULE010 elements
        /// and return the FIRST node along that chain that carries another strut
        /// foot. Intermediate subdivision nodes without struts are skipped.
        ///
        /// The rule connects strut tip → strut tip only. If a strut has no
        /// neighbour in a direction (chain end with no further strut), no brace
        /// is added in that direction. Option (0 = none for this stud,
        /// 1 = previous neighbour, 2 = next neighbour, 3 = both) from the D-gene
        /// mapped to the integer domain; previous / next are disambiguated by
        /// projecting the neighbour foot onto the parent beam's direction.
        /// </summary>
        public override string RuleOperation(ref SG_Shape ss_ref, ref SG_Genotype gt)
        {
            SH_CrossSection_Beam def_crosec = ss_ref.Elems?
                .OfType<SG_Elem1D>()
                .FirstOrDefault()?.CrossSection;
            if (def_crosec == null)
            {
                var fallback = new SH_CrossSection_Rectangle(10, 10);
                fallback.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
                def_crosec = fallback;
            }

            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, UT.RULE051_MARKER);
            if (sid == -999 || eid == -999)
                return "Autorule051-3D - wrong marker";

            var selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            var selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);
            int geneCount = selectedIntGenes.Count;
            if (geneCount == 0)
                return "Autorule051-3D - empty gene segment";

            double range = Domain[1] - Domain[0];

            var studElements = ss_ref.Elems
                .OfType<SG_Elem1D>()
                .Where(e => e.Name == "3DAR2")
                .ToList();
            if (studElements.Count == 0)
                return "Auto-rule 051-3D: no struts";

            // node → list of RULE010 sub-elements incident to that node.
            // This is the actual base-beam subdivision graph: walking it via
            // shared nodes is independent of any Curve reference identity.
            var nodeToR010 = new Dictionary<SG_Node, List<SG_Elem1D>>();
            foreach (var e in ss_ref.Elems.OfType<SG_Elem1D>()
                         .Where(x => x.Autorule == UT.RULE010_MARKER))
            {
                if (e.Nodes == null) continue;
                foreach (var nd in e.Nodes)
                {
                    if (nd == null) continue;
                    if (!nodeToR010.TryGetValue(nd, out var lst))
                    {
                        lst = new List<SG_Elem1D>();
                        nodeToR010[nd] = lst;
                    }
                    lst.Add(e);
                }
            }

            // Foot-node → strut. Multiple struts at one foot are not expected;
            // first-wins keeps behaviour deterministic.
            var footToStud = new Dictionary<SG_Node, SG_Elem1D>();
            foreach (var s in studElements)
            {
                if (s.Nodes != null && s.Nodes.Length >= 2 && s.Nodes[0] != null
                    && !footToStud.ContainsKey(s.Nodes[0]))
                    footToStud[s.Nodes[0]] = s;
            }

            // Walks the RULE010 subdivision chain from `start` through `firstElem`
            // and returns the first node (other than start) that hosts a strut foot.
            // Intermediate nodes without struts are skipped. Returns null if the
            // chain ends without finding a strut.
            SG_Node WalkToNeighbourFoot(SG_Node start, SG_Elem1D firstElem)
            {
                if (firstElem?.Nodes == null || firstElem.Nodes.Length < 2) return null;
                var visited = new HashSet<SG_Elem1D> { firstElem };
                SG_Node prev = start;
                SG_Elem1D curr = firstElem;
                for (int safety = 0; safety < 100000; safety++)
                {
                    var other = ReferenceEquals(curr.Nodes[0], prev) ? curr.Nodes[1] : curr.Nodes[0];
                    if (other == null) return null;
                    if (footToStud.ContainsKey(other)) return other;

                    if (!nodeToR010.TryGetValue(other, out var nextList)) return null;
                    var nextElem = nextList.FirstOrDefault(x => !visited.Contains(x));
                    if (nextElem == null) return null;

                    visited.Add(nextElem);
                    prev = other;
                    curr = nextElem;
                }
                return null;
            }

            // For each strut, find prev/next neighbour foot along the rule010 chain.
            // prev/next is decided by projecting the neighbour foot onto the parent
            // beam's direction (Joined_Init_Crv if available, else Init_Crv, else
            // a fallback world axis).
            var prevFoot = new Dictionary<SG_Elem1D, SG_Node>();
            var nextFoot = new Dictionary<SG_Elem1D, SG_Node>();
            foreach (var stud in studElements)
            {
                if (stud.Nodes == null || stud.Nodes.Length < 2) continue;
                var foot = stud.Nodes[0];
                if (foot == null) continue;
                if (!nodeToR010.TryGetValue(foot, out var r010s)) continue;
                if (r010s.Count == 0) continue;

                // Reference axis from the parent beam direction.
                Vector3d axis = Vector3d.XAxis;
                Curve refCrv = r010s[0].Joined_Init_Crv ?? r010s[0].Init_Crv;
                if (refCrv != null)
                {
                    var v = refCrv.PointAtEnd - refCrv.PointAtStart;
                    if (v.SquareLength > 1e-24 && v.Unitize()) axis = v;
                }
                double Project(Point3d p) => (p - Point3d.Origin) * axis;
                double tFoot = Project(foot.Pt);

                foreach (var r010 in r010s)
                {
                    var nb = WalkToNeighbourFoot(foot, r010);
                    if (nb == null) continue;
                    double tNb = Project(nb.Pt);
                    if (tNb < tFoot - UT.PRES * 0.5)
                    {
                        // Keep the closest below tFoot
                        if (!prevFoot.TryGetValue(stud, out var ex) || ex == null
                            || tNb > Project(ex.Pt))
                            prevFoot[stud] = nb;
                    }
                    else if (tNb > tFoot + UT.PRES * 0.5)
                    {
                        // Keep the closest above tFoot
                        if (!nextFoot.TryGetValue(stud, out var ex) || ex == null
                            || tNb < Project(ex.Pt))
                            nextFoot[stud] = nb;
                    }
                }
            }

            int addedCount = 0;
            var activated = new bool[studElements.Count];
            var eligible = new List<int>();

            for (int i = 0; i < studElements.Count; i++)
            {
                var stud = studElements[i];
                int geneIdx = i % geneCount;
                bool shouldActivate = selectedIntGenes[geneIdx] != 0;

                double optionDbl = selectedDGenes[geneIdx] * range + Domain[0];
                int optionNumber = (int)Math.Round(optionDbl, MidpointRounding.AwayFromZero);

                // 0 = no braces for this stud (domain must include 0, e.g. [0,3])
                if (optionNumber == 0)
                    continue;

                bool hasPrev = TryGetTip(prevFoot, footToStud, stud, out Point3d prevTipPt);
                bool hasNext = TryGetTip(nextFoot, footToStud, stud, out Point3d nextTipPt);

                var targets = new List<Point3d>();
                switch (optionNumber)
                {
                    case 1:
                        if (hasPrev) targets.Add(prevTipPt);
                        break;
                    case 2:
                        if (hasNext) targets.Add(nextTipPt);
                        break;
                    case 3:
                        if (hasPrev) targets.Add(prevTipPt);
                        if (hasNext) targets.Add(nextTipPt);
                        break;
                    default:
                        continue;
                }
                if (targets.Count > 0) eligible.Add(i);
                if (!shouldActivate) continue;

                var studTip = stud.Nodes[1].Pt;
                foreach (var targetPt in targets)
                {
                    var ln = new Line(studTip, targetPt);
                    if (!ln.IsValid || ln.Length <= UT.PRES) continue;
                    // The "prev of j" / "next of i" relation is reciprocal so
                    // adjacent struts would otherwise add the same brace twice.
                    if (Rule06xHelper.LineAlreadyPresent(ss_ref, ln)) continue;

                    ss_ref.AddNewElement(new SG_Elem1D(ln, -999, "3DAR51", def_crosec)
                    {
                        Autorule = UT.RULE051_MARKER
                    });
                    addedCount++;
                    activated[i] = true;
                }
            }

            if (MinRatio > 0.0 && eligible.Count > 0)
            {
                int activeCount = eligible.Count(idx => activated[idx]);
                int target = (int)Math.Ceiling(MinRatio * eligible.Count);
                for (int i = 0; i < studElements.Count && activeCount < target; i++)
                {
                    if (!eligible.Contains(i) || activated[i]) continue;
                    var stud = studElements[i];
                    bool hasPrev = TryGetTip(prevFoot, footToStud, stud, out Point3d prevTipPt);
                    bool hasNext = TryGetTip(nextFoot, footToStud, stud, out Point3d nextTipPt);
                    var fallbackTargets = new List<Point3d>();
                    if (hasPrev) fallbackTargets.Add(prevTipPt);
                    if (hasNext) fallbackTargets.Add(nextTipPt);
                    var studTip = stud.Nodes[1].Pt;
                    foreach (var targetPt in fallbackTargets)
                    {
                        var ln = new Line(studTip, targetPt);
                        if (!ln.IsValid || ln.Length <= UT.PRES) continue;
                        if (Rule06xHelper.LineAlreadyPresent(ss_ref, ln)) continue;
                        ss_ref.AddNewElement(new SG_Elem1D(ln, -999, "3DAR51", def_crosec)
                        { Autorule = UT.RULE051_MARKER });
                        addedCount++;
                        activated[i] = true;
                        activeCount++;
                        break;
                    }
                }
            }

            return $"Auto-rule 051-3D: {addedCount} braces added from {studElements.Count} studs (chain mode)";
        }

        private static bool TryGetTip(
            Dictionary<SG_Elem1D, SG_Node> map,
            Dictionary<SG_Node, SG_Elem1D> footToStud,
            SG_Elem1D stud,
            out Point3d tip)
        {
            tip = Point3d.Origin;
            if (!map.TryGetValue(stud, out var foot) || foot == null) return false;
            if (!footToStud.TryGetValue(foot, out var nbStud) || nbStud == null) return false;
            if (nbStud.Nodes == null || nbStud.Nodes.Length < 2 || nbStud.Nodes[1] == null) return false;
            tip = nbStud.Nodes[1].Pt;
            return true;
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }
    }
}
