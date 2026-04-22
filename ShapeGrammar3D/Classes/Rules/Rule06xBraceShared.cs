using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Shared brace-building logic used by rule062 (between rule061 ties) and
    /// rule064 (between rule061 and rule063 ties).
    /// </summary>
    internal static class Rule06xBraceShared
    {
        /// <summary>
        /// Single tie identified by two strut endpoints (tip or foot) and its
        /// two base curves.
        /// </summary>
        private sealed class Tie
        {
            public SG_Elem1D Beam;
            public Point3d PtA;      // endpoint on base curve A (= pair key First)
            public Point3d PtB;      // endpoint on base curve B (= pair key Second)
            public double TOnA;      // sort key along A
        }

        // ==========================================================================
        // rule062: brace between consecutive rule061 ties on the same base pair
        // ==========================================================================

        public static string ApplyTieBrace(
            SG_Shape ss,
            SG_Genotype gt,
            int ruleMarker,
            int sourceMarker,
            bool useTip,
            string elemName,
            string label,
            int[] domain,
            double minRatio = 0.0)
        {
            var def_crosec = ResolveDefaultCrossSection(ss);
            if (!TryReadGeneSegment(gt, ruleMarker, out var intGenes, out var dGenes, out string err))
                return $"Autorule{label}-3D - {err}";
            int option = DecodeOption(dGenes, domain);

            var studInfos = Rule06xHelper.CollectStudBaseInfos(ss);
            if (studInfos.Count == 0)
                return $"Auto-rule {label}-3D: no struts found";

            // Map each strut tip/foot node to its base-curve info (reference equality).
            var nodeToInfo = BuildNodeToInfoMap(studInfos, useTip);

            // Collect source ties (rule061) grouped by unordered base-curve pair.
            var groups = CollectTiesByPair(ss, sourceMarker, nodeToInfo);
            if (groups.Count == 0)
                return $"Auto-rule {label}-3D: no source ties (rule{sourceMarker:D3}) found";

            var candidates = new List<(Point3d A, Point3d B)>();
            foreach (var kv in groups)
            {
                var ties = kv.Value;
                ties.Sort((a, b) => a.TOnA.CompareTo(b.TOnA));

                var aCurve = kv.Key.First;
                var bCurve = kv.Key.Second;

                // Determine whether the B side is traversed in reverse when A is
                // traversed forward (i.e. ties cross vs ladder-like).
                bool reversedB = DetectReversedB(ties, bCurve);

                Point3d aStart = aCurve.PointAtStart;
                Point3d aEnd = aCurve.PointAtEnd;
                Point3d bStart = reversedB ? bCurve.PointAtEnd : bCurve.PointAtStart;
                Point3d bEnd = reversedB ? bCurve.PointAtStart : bCurve.PointAtEnd;

                // Virtual tie at the start — closes the quadrilateral with ties[0].
                if (ties.Count > 0)
                {
                    var t0 = ties[0];
                    candidates.Add((aStart, t0.PtB));
                    if (option >= 2)
                        candidates.Add((bStart, t0.PtA));
                }

                for (int i = 0; i < ties.Count - 1; i++)
                {
                    var ti = ties[i];
                    var tj = ties[i + 1];
                    candidates.Add((ti.PtA, tj.PtB));
                    if (option >= 2)
                        candidates.Add((ti.PtB, tj.PtA));
                }

                // Virtual tie at the end — closes with ties[last].
                if (ties.Count > 0)
                {
                    var tN = ties[ties.Count - 1];
                    candidates.Add((tN.PtA, bEnd));
                    if (option >= 2)
                        candidates.Add((tN.PtB, aEnd));
                }
            }
            int addedCount = AddWithMinRatio(ss, candidates, intGenes, minRatio, elemName, ruleMarker, def_crosec);
            return $"Auto-rule {label}-3D: {addedCount} braces added (option={option})";
        }

        // ==========================================================================
        // rule064: brace between corresponding rule061 (top) and rule063 (bottom) ties
        // ==========================================================================

        public static string ApplyTopBottomBrace(
            SG_Shape ss,
            SG_Genotype gt,
            int ruleMarker,
            int topMarker,
            int bottomMarker,
            string elemName,
            string label,
            int[] domain,
            double minRatio = 0.0)
        {
            var def_crosec = ResolveDefaultCrossSection(ss);
            if (!TryReadGeneSegment(gt, ruleMarker, out var intGenes, out var dGenes, out string err))
                return $"Autorule{label}-3D - {err}";
            int option = DecodeOption(dGenes, domain);

            var studInfos = Rule06xHelper.CollectStudBaseInfos(ss);
            if (studInfos.Count == 0)
                return $"Auto-rule {label}-3D: no struts found";

            var tipNodeToInfo = BuildNodeToInfoMap(studInfos, useTip: true);
            var footNodeToInfo = BuildNodeToInfoMap(studInfos, useTip: false);

            var topGroups = CollectTiesByPair(ss, topMarker, tipNodeToInfo);
            var bottomGroups = CollectTiesByPair(ss, bottomMarker, footNodeToInfo);
            if (topGroups.Count == 0 || bottomGroups.Count == 0)
                return $"Auto-rule {label}-3D: top or bottom ties missing";

            var candidates = new List<(Point3d A, Point3d B)>();
            foreach (var kv in topGroups)
            {
                if (!bottomGroups.TryGetValue(kv.Key, out var bottomTies)) continue;
                var topTies = kv.Value;
                topTies.Sort((a, b) => a.TOnA.CompareTo(b.TOnA));
                bottomTies.Sort((a, b) => a.TOnA.CompareTo(b.TOnA));

                int n = Math.Min(topTies.Count, bottomTies.Count);
                for (int i = 0; i < n; i++)
                {
                    var top = topTies[i];
                    var bot = bottomTies[i];
                    // Quadrilateral: topA, topB, bottomB, bottomA.
                    // Diagonals: topA ↔ bottomB and topB ↔ bottomA.
                    candidates.Add((top.PtA, bot.PtB));
                    if (option >= 2)
                        candidates.Add((top.PtB, bot.PtA));
                }
            }
            int addedCount = AddWithMinRatio(ss, candidates, intGenes, minRatio, elemName, ruleMarker, def_crosec);
            return $"Auto-rule {label}-3D: {addedCount} braces added (option={option})";
        }

        // ==========================================================================
        // Helpers
        // ==========================================================================

        private static SH_CrossSection_Beam ResolveDefaultCrossSection(SG_Shape ss)
        {
            var def = ss.Elems?.OfType<SG_Elem1D>().FirstOrDefault()?.CrossSection;
            if (def != null) return def;
            var fallback = new SH_CrossSection_Rectangle(10, 10);
            fallback.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
            return fallback;
        }

        private static bool TryReadGeneSegment(SG_Genotype gt, int ruleMarker,
            out List<int> intGenes, out List<double> dGenes, out string err)
        {
            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, ruleMarker);
            if (sid == -999 || eid == -999)
            {
                intGenes = null;
                dGenes = null;
                err = "wrong marker";
                return false;
            }
            intGenes = gt.IntGenes.GetRange(sid, eid - sid);
            dGenes = gt.DGenes.GetRange(sid, eid - sid);
            if (intGenes.Count == 0)
            {
                err = "empty gene segment";
                return false;
            }
            err = null;
            return true;
        }

        private static int DecodeOption(List<double> dGenes, int[] domain)
        {
            double min = domain != null && domain.Length > 0 ? domain[0] : 1;
            double max = domain != null && domain.Length > 1 ? domain[1] : 2;
            double range = max - min;
            double dbl = (dGenes.Count > 0 ? dGenes[0] : 0.0) * range + min;
            int opt = (int)Math.Round(dbl, MidpointRounding.AwayFromZero);
            if (opt < 1) opt = 1;
            if (opt > 2) opt = 2;
            return opt;
        }

        private static int AddWithMinRatio(
            SG_Shape ss,
            List<(Point3d A, Point3d B)> candidates,
            List<int> intGenes,
            double minRatio,
            string elemName,
            int ruleMarker,
            SH_CrossSection_Beam def_crosec)
        {
            if (candidates == null || candidates.Count == 0) return 0;
            if (intGenes == null || intGenes.Count == 0) return 0;
            minRatio = Math.Clamp(minRatio, 0.0, 1.0);

            int added = 0;
            var activated = new bool[candidates.Count];
            int geneCount = intGenes.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                int geneIdx = i % geneCount;
                if (intGenes[geneIdx] == 0) continue;
                if (AddDiagonal(ss, candidates[i].A, candidates[i].B, elemName, ruleMarker, def_crosec) > 0)
                {
                    activated[i] = true;
                    added++;
                }
            }

            if (minRatio <= 0.0) return added;
            int activeCount = activated.Count(x => x);
            int target = (int)Math.Ceiling(minRatio * candidates.Count);
            for (int i = 0; i < candidates.Count && activeCount < target; i++)
            {
                if (activated[i]) continue;
                if (AddDiagonal(ss, candidates[i].A, candidates[i].B, elemName, ruleMarker, def_crosec) > 0)
                {
                    activated[i] = true;
                    activeCount++;
                    added++;
                }
            }
            return added;
        }

        private static Dictionary<SG_Node, Rule06xHelper.StudBaseInfo> BuildNodeToInfoMap(
            List<Rule06xHelper.StudBaseInfo> infos, bool useTip)
        {
            var map = new Dictionary<SG_Node, Rule06xHelper.StudBaseInfo>();
            foreach (var info in infos)
            {
                var node = useTip ? info.Stud.Nodes[1] : info.Stud.Nodes[0];
                if (node == null) continue;
                map[node] = info;
            }
            return map;
        }

        private static Dictionary<Rule06xHelper.CurvePairKey, List<Tie>> CollectTiesByPair(
            SG_Shape ss, int sourceMarker, Dictionary<SG_Node, Rule06xHelper.StudBaseInfo> nodeToInfo)
        {
            var groups = new Dictionary<Rule06xHelper.CurvePairKey, List<Tie>>();
            var beams = ss.Elems.OfType<SG_Elem1D>().Where(e => e.Autorule == sourceMarker).ToList();
            foreach (var b in beams)
            {
                if (b.Nodes == null || b.Nodes.Length < 2 || b.Nodes[0] == null || b.Nodes[1] == null)
                    continue;
                if (!nodeToInfo.TryGetValue(b.Nodes[0], out var info0)) continue;
                if (!nodeToInfo.TryGetValue(b.Nodes[1], out var info1)) continue;
                if (ReferenceEquals(info0.BaseCurve, info1.BaseCurve)) continue;

                var key = new Rule06xHelper.CurvePairKey(info0.BaseCurve, info1.BaseCurve);
                bool info0IsA = ReferenceEquals(info0.BaseCurve, key.First);
                var tie = new Tie
                {
                    Beam = b,
                    PtA = info0IsA ? b.Nodes[0].Pt : b.Nodes[1].Pt,
                    PtB = info0IsA ? b.Nodes[1].Pt : b.Nodes[0].Pt,
                    TOnA = info0IsA ? info0.FootT : info1.FootT
                };
                if (!groups.TryGetValue(key, out var lst))
                {
                    lst = new List<Tie>();
                    groups[key] = lst;
                }
                lst.Add(tie);
            }
            return groups;
        }

        private static bool DetectReversedB(List<Tie> ties, Curve bCurve)
        {
            if (ties.Count < 2) return false;
            // Compute tOnB for first and last tie; if not monotonically increasing,
            // B is traversed in reverse relative to A.
            if (!bCurve.ClosestPoint(ties[0].PtB, out double tb0)) return false;
            if (!bCurve.ClosestPoint(ties[ties.Count - 1].PtB, out double tbN)) return false;
            return tb0 > tbN;
        }

        private static int AddDiagonal(SG_Shape ss, Point3d a, Point3d b, string elemName,
            int ruleMarker, SH_CrossSection_Beam def_crosec)
        {
            var ln = new Line(a, b);
            if (!ln.IsValid || ln.Length <= UT.PRES) return 0;
            if (Rule06xHelper.LineAlreadyPresent(ss, ln)) return 0;

            var beam = new SG_Elem1D(ln, -999, elemName, def_crosec)
            {
                Autorule = ruleMarker
            };
            ss.AddNewElement(beam);
            return 1;
        }
    }
}
