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
        /// Adds braces between each RULE020 strut tip and the tip of its neighbouring
        /// strut(s) along the shared RULE010 base beam.
        ///
        /// For every strut we locate its base curve (RULE010 Joined_Init_Crv) and the
        /// parameter t of its foot on that curve. Studs that share the same base curve
        /// are sorted by t and only the immediately adjacent ones are candidates. The
        /// option (1 = previous neighbour, 2 = next neighbour, 3 = both) is wrapped from
        /// the D-gene. End struts connect to the single available neighbour only.
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
            var activated = new bool[studElements.Count];
            var eligible = new List<int>();

            // Gather (stud, base curve, base param t) for every strut that has a valid
            // RULE010 parent curve at its foot.
            var studData = new List<StudLocation>();
            foreach (var se in studElements)
            {
                if (se.Nodes == null || se.Nodes.Length < 2 || se.Nodes[0] == null || se.Nodes[1] == null)
                    continue;

                var baseElem = se.Nodes[0].Elements
                    .OfType<SG_Elem1D>()
                    .FirstOrDefault(e => e.Autorule == UT.RULE010_MARKER);
                if (baseElem == null) continue;

                var baseCrv = baseElem.Joined_Init_Crv;
                if (baseCrv == null) continue;
                if (!baseCrv.ClosestPoint(se.Nodes[0].Pt, out double t)) continue;

                studData.Add(new StudLocation(se, baseCrv, t));
            }

            // Group by base curve identity (reference equality on the Curve instance)
            // and sort each group by t so neighbours are adjacent indices.
            var groups = new Dictionary<Curve, List<StudLocation>>(CurveReferenceComparer.Instance);
            foreach (var s in studData)
            {
                if (!groups.TryGetValue(s.BaseCurve, out var lst))
                {
                    lst = new List<StudLocation>();
                    groups[s.BaseCurve] = lst;
                }
                lst.Add(s);
            }

            var studLookup = new Dictionary<SG_Elem1D, (List<StudLocation> group, int index)>();
            foreach (var kv in groups)
            {
                kv.Value.Sort((a, b) => a.T.CompareTo(b.T));
                for (int i = 0; i < kv.Value.Count; i++)
                    studLookup[kv.Value[i].Stud] = (kv.Value, i);
            }

            int addedCount = 0;
            for (int i = 0; i < studElements.Count; i++)
            {
                int geneIdx = i % geneCount;
                bool shouldActivate = selectedIntGenes[geneIdx] != 0;

                double optionDbl = selectedDGenes[geneIdx] * range + Domain[0];
                int optionNumber = (int)Math.Round(optionDbl, MidpointRounding.AwayFromZero);

                var stud0 = studElements[i];
                if (!studLookup.TryGetValue(stud0, out var loc)) continue;

                var group = loc.group;
                int idx = loc.index;

                SG_Node prevTip = idx > 0 ? group[idx - 1].Stud.Nodes[1] : null;
                SG_Node nextTip = idx < group.Count - 1 ? group[idx + 1].Stud.Nodes[1] : null;

                var targets = new List<SG_Node>();
                if (prevTip == null && nextTip == null) continue;

                if (prevTip == null)
                {
                    // Start of base curve: only next neighbour available.
                    targets.Add(nextTip);
                }
                else if (nextTip == null)
                {
                    // End of base curve: only previous neighbour available.
                    targets.Add(prevTip);
                }
                else
                {
                    switch (optionNumber)
                    {
                        case 1: targets.Add(prevTip); break;
                        case 2: targets.Add(nextTip); break;
                        case 3:
                            targets.Add(prevTip);
                            targets.Add(nextTip);
                            break;
                        default: continue;
                    }
                }
                if (targets.Count > 0) eligible.Add(i);

                foreach (var tip in targets)
                {
                    if (!shouldActivate) break;
                    if (tip == null) continue;
                    var ln = new Line(stud0.Nodes[1].Pt, tip.Pt);
                    if (!ln.IsValid || ln.Length <= UT.PRES) continue;
                    // Guard against duplicate braces: the "prev of j" / "next of i"
                    // relation is reciprocal so adjacent struts would otherwise add
                    // the same brace twice.
                    if (Rule06xHelper.LineAlreadyPresent(ss_ref, ln)) continue;

                    var brace = new SG_Elem1D(ln, -999, "3DAR51", def_crosec)
                    {
                        Autorule = UT.RULE051_MARKER
                    };
                    ss_ref.AddNewElement(brace);
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
                    var stud0 = studElements[i];
                    if (!studLookup.TryGetValue(stud0, out var loc)) continue;
                    var group = loc.group;
                    int idx = loc.index;
                    SG_Node prevTip = idx > 0 ? group[idx - 1].Stud.Nodes[1] : null;
                    SG_Node nextTip = idx < group.Count - 1 ? group[idx + 1].Stud.Nodes[1] : null;
                    var fallbackTargets = new List<SG_Node>();
                    if (prevTip != null) fallbackTargets.Add(prevTip);
                    if (nextTip != null) fallbackTargets.Add(nextTip);
                    foreach (var tip in fallbackTargets)
                    {
                        if (tip == null) continue;
                        var ln = new Line(stud0.Nodes[1].Pt, tip.Pt);
                        if (!ln.IsValid || ln.Length <= UT.PRES) continue;
                        if (Rule06xHelper.LineAlreadyPresent(ss_ref, ln)) continue;
                        ss_ref.AddNewElement(new SG_Elem1D(ln, -999, "3DAR51", def_crosec) { Autorule = UT.RULE051_MARKER });
                        addedCount++;
                        activated[i] = true;
                        activeCount++;
                        break;
                    }
                }
            }

            return $"Auto-rule 051-3D: {addedCount} braces added from {studElements.Count} studs (neighbour mode)";
        }

        public override State GetNextState()
        {
            throw new NotImplementedException();
        }

        private sealed class StudLocation
        {
            public SG_Elem1D Stud { get; }
            public Curve BaseCurve { get; }
            public double T { get; }

            public StudLocation(SG_Elem1D stud, Curve baseCurve, double t)
            {
                Stud = stud;
                BaseCurve = baseCurve;
                T = t;
            }
        }

        private sealed class CurveReferenceComparer : IEqualityComparer<Curve>
        {
            public static readonly CurveReferenceComparer Instance = new CurveReferenceComparer();
            public bool Equals(Curve x, Curve y) => ReferenceEquals(x, y);
            public int GetHashCode(Curve obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
