using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Shared implementation of rule061 / rule063. The two rules only differ in
    /// whether they connect strut tips (Nodes[1]) or strut feet (Nodes[0]).
    ///
    /// For every focus strut we:
    /// <list type="bullet">
    /// <item>Split all OTHER InitShape base beams into a "left" and a "right" set
    ///   using a perpendicular measured at the focus strut's foot (local tangent
    ///   crossed with world-Z).</item>
    /// <item>Pick the single nearest base beam on each side (the "adjacent" beams,
    ///   at most 2 total).</item>
    /// <item>On each adjacent beam, pick the strut whose tip/foot is closest to
    ///   the focus strut's tip/foot.</item>
    /// <item>Connect the focus strut's tip/foot to the chosen candidate(s)
    ///   according to the option gene: 1 = left only, 2 = right only, 3 = both.</item>
    /// </list>
    /// </summary>
    internal static class Rule06xShared
    {
        public static string ApplyTipOrFootConnector(
            SG_Shape ss,
            SG_Genotype gt,
            int ruleMarker,
            bool useTip,
            string elemName,
            string label,
            int[] domain,
            double minRatio = 0.0)
        {
            SH_CrossSection_Beam def_crosec = ss.Elems?
                .OfType<SG_Elem1D>()
                .FirstOrDefault()?.CrossSection;
            if (def_crosec == null)
            {
                var fallback = new SH_CrossSection_Rectangle(10, 10);
                fallback.Material = (SH_Material)SH_Material_Isotrop.Default_Material();
                def_crosec = fallback;
            }

            int sid = -999, eid = -999;
            gt.FindRange(ref sid, ref eid, ruleMarker);
            if (sid == -999 || eid == -999)
                return $"Autorule{label}-3D - wrong marker";

            var selectedIntGenes = gt.IntGenes.GetRange(sid, eid - sid);
            var selectedDGenes = gt.DGenes.GetRange(sid, eid - sid);
            int geneCount = selectedIntGenes.Count;
            if (geneCount == 0)
                return $"Autorule{label}-3D - empty gene segment";

            // Option domain: [1, 3] by convention (1 = left, 2 = right, 3 = both).
            double domMin = domain != null && domain.Length > 0 ? domain[0] : 1;
            double domMax = domain != null && domain.Length > 1 ? domain[1] : 3;
            double domRange = domMax - domMin;

            var allInfos = Rule06xHelper.CollectStudBaseInfos(ss);
            if (allInfos.Count == 0)
                return $"Auto-rule {label}-3D: no studs available";

            var groups = Rule06xHelper.GroupByBaseCurve(allInfos);
            var studToInfo = new Dictionary<SG_Elem1D, Rule06xHelper.StudBaseInfo>();
            foreach (var info in allInfos)
                studToInfo[info.Stud] = info;

            var studElements = ss.Elems
                .OfType<SG_Elem1D>()
                .Where(e => e.Name == "3DAR2")
                .ToList();

            int addedCount = 0;
            var activated = new bool[studElements.Count];
            var eligible = new List<int>();
            for (int i = 0; i < studElements.Count; i++)
            {
                int geneIdx = i % geneCount;
                bool shouldActivate = selectedIntGenes[geneIdx] != 0;

                double optDbl = selectedDGenes[geneIdx] * domRange + domMin;
                int optionNumber = (int)Math.Round(optDbl, MidpointRounding.AwayFromZero);
                if (optionNumber < 1 || optionNumber > 3) continue;

                var stud0 = studElements[i];
                if (!studToInfo.TryGetValue(stud0, out var info0)) continue;

                Point3d anchor = useTip ? stud0.Nodes[1].Pt : stud0.Nodes[0].Pt;
                Point3d foot0 = stud0.Nodes[0].Pt;

                // Local tangent at the foot, projected onto the horizontal to build
                // a repeatable "left/right" perpendicular via cross-product with Z.
                var tangent = info0.BaseCurve.TangentAt(info0.FootT);
                if (!tangent.Unitize()) continue;
                var perp = Vector3d.CrossProduct(tangent, Vector3d.ZAxis);
                if (perp.SquareLength < 1e-12)
                    perp = Vector3d.CrossProduct(tangent, Vector3d.XAxis);
                if (!perp.Unitize()) continue;

                SG_Elem1D leftStud = null; double leftDist = double.PositiveInfinity;
                SG_Elem1D rightStud = null; double rightDist = double.PositiveInfinity;

                foreach (var kv in groups)
                {
                    if (ReferenceEquals(kv.Key, info0.BaseCurve)) continue;
                    var candidates = kv.Value;
                    if (candidates.Count == 0) continue;

                    // Side is decided by the offset between foot0 and the nearest
                    // point on this other base curve.
                    if (!kv.Key.ClosestPoint(foot0, out double ot)) continue;
                    var otherPt = kv.Key.PointAt(ot);
                    var offset = otherPt - foot0;
                    double sideSign = offset * perp; // dot product
                    double beamDist = offset.Length;

                    // Pick the single closest candidate on this beam (to the anchor).
                    Rule06xHelper.StudBaseInfo best = null;
                    double bestDist = double.PositiveInfinity;
                    foreach (var cand in candidates)
                    {
                        Point3d candPt = useTip ? cand.Stud.Nodes[1].Pt : cand.Stud.Nodes[0].Pt;
                        double d = candPt.DistanceTo(anchor);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = cand;
                        }
                    }
                    if (best == null) continue;

                    if (sideSign >= 0)
                    {
                        if (beamDist < rightDist)
                        {
                            rightDist = beamDist;
                            rightStud = best.Stud;
                        }
                    }
                    else
                    {
                        if (beamDist < leftDist)
                        {
                            leftDist = beamDist;
                            leftStud = best.Stud;
                        }
                    }
                }

                var targets = new List<(SG_Elem1D stud, bool isRightSide)>();
                if (optionNumber == 1 && leftStud != null) targets.Add((leftStud, false));
                else if (optionNumber == 2 && rightStud != null) targets.Add((rightStud, true));
                else if (optionNumber == 3)
                {
                    if (leftStud != null) targets.Add((leftStud, false));
                    if (rightStud != null) targets.Add((rightStud, true));
                }
                if (targets.Count > 0) eligible.Add(i);

                foreach (var t in targets)
                {
                    if (!shouldActivate) break;
                    var target = t.stud;
                    bool isRightSideAtAnchor = t.isRightSide;
                    Point3d targetPt = useTip ? target.Nodes[1].Pt : target.Nodes[0].Pt;
                    var newLine = new Line(anchor, targetPt);
                    if (!newLine.IsValid || newLine.Length <= UT.PRES) continue;
                    if (Rule06xHelper.LineAlreadyPresent(ss, newLine)) continue;
                    // Enforce "at most one beam per side from one point" even when
                    // reciprocal generation tries to add another beam later.
                    if (HasSideConnectionAtPoint(ss, anchor, perp, isRightSideAtAnchor, ruleMarker))
                        continue;

                    // Also enforce the same rule at the target endpoint from the
                    // target strut's local left/right frame.
                    if (studToInfo.TryGetValue(target, out var targetInfo))
                    {
                        var targetFoot = target.Nodes[0].Pt;
                        var tgtTan = targetInfo.BaseCurve.TangentAt(targetInfo.FootT);
                        if (tgtTan.Unitize())
                        {
                            var tgtPerp = Vector3d.CrossProduct(tgtTan, Vector3d.ZAxis);
                            if (tgtPerp.SquareLength < 1e-12)
                                tgtPerp = Vector3d.CrossProduct(tgtTan, Vector3d.XAxis);
                            if (tgtPerp.Unitize())
                            {
                                bool isRightAtTarget = ((anchor - targetFoot) * tgtPerp) >= 0.0;
                                if (HasSideConnectionAtPoint(ss, targetPt, tgtPerp, isRightAtTarget, ruleMarker))
                                    continue;
                            }
                        }
                    }

                    var newBeam = new SG_Elem1D(newLine, -999, elemName, def_crosec)
                    {
                        Autorule = ruleMarker
                    };
                    ss.AddNewElement(newBeam);
                    addedCount++;
                    activated[i] = true;
                }
            }

            minRatio = Math.Clamp(minRatio, 0.0, 1.0);
            if (minRatio > 0.0 && eligible.Count > 0)
            {
                int activeCount = eligible.Count(idx => activated[idx]);
                int target = (int)Math.Ceiling(minRatio * eligible.Count);
                for (int i = 0; i < studElements.Count && activeCount < target; i++)
                {
                    if (!eligible.Contains(i) || activated[i]) continue;
                    var stud0 = studElements[i];
                    if (!studToInfo.TryGetValue(stud0, out var info0)) continue;

                    Point3d anchor = useTip ? stud0.Nodes[1].Pt : stud0.Nodes[0].Pt;
                    var tangent = info0.BaseCurve.TangentAt(info0.FootT);
                    if (!tangent.Unitize()) continue;
                    var perp = Vector3d.CrossProduct(tangent, Vector3d.ZAxis);
                    if (perp.SquareLength < 1e-12)
                        perp = Vector3d.CrossProduct(tangent, Vector3d.XAxis);
                    if (!perp.Unitize()) continue;

                    SG_Elem1D leftStud = null; double leftDist = double.PositiveInfinity;
                    SG_Elem1D rightStud = null; double rightDist = double.PositiveInfinity;
                    foreach (var kv in groups)
                    {
                        if (ReferenceEquals(kv.Key, info0.BaseCurve)) continue;
                        var candidates = kv.Value;
                        if (candidates.Count == 0) continue;
                        if (!kv.Key.ClosestPoint(stud0.Nodes[0].Pt, out double ot)) continue;
                        var otherPt = kv.Key.PointAt(ot);
                        var offset = otherPt - stud0.Nodes[0].Pt;
                        double sideSign = offset * perp;
                        double beamDist = offset.Length;
                        Rule06xHelper.StudBaseInfo best = null;
                        double bestDist = double.PositiveInfinity;
                        foreach (var cand in candidates)
                        {
                            Point3d candPt = useTip ? cand.Stud.Nodes[1].Pt : cand.Stud.Nodes[0].Pt;
                            double d = candPt.DistanceTo(anchor);
                            if (d < bestDist) { bestDist = d; best = cand; }
                        }
                        if (best == null) continue;
                        if (sideSign >= 0) { if (beamDist < rightDist) { rightDist = beamDist; rightStud = best.Stud; } }
                        else { if (beamDist < leftDist) { leftDist = beamDist; leftStud = best.Stud; } }
                    }

                    var fallbackTargets = new List<(SG_Elem1D stud, bool isRightSide)>();
                    if (leftStud != null) fallbackTargets.Add((leftStud, false));
                    if (rightStud != null) fallbackTargets.Add((rightStud, true));
                    foreach (var t in fallbackTargets)
                    {
                        var targetStud = t.stud;
                        var targetPt = useTip ? targetStud.Nodes[1].Pt : targetStud.Nodes[0].Pt;
                        var newLine = new Line(anchor, targetPt);
                        if (!newLine.IsValid || newLine.Length <= UT.PRES) continue;
                        if (Rule06xHelper.LineAlreadyPresent(ss, newLine)) continue;
                        if (HasSideConnectionAtPoint(ss, anchor, perp, t.isRightSide, ruleMarker)) continue;
                        var beam = new SG_Elem1D(newLine, -999, elemName, def_crosec) { Autorule = ruleMarker };
                        ss.AddNewElement(beam);
                        addedCount++;
                        activated[i] = true;
                        activeCount++;
                        break;
                    }
                }
            }

            string chord = useTip ? "top chord" : "bottom chord";
            return $"Auto-rule {label}-3D: {addedCount} {chord} ties added from {studElements.Count} studs";
        }

        /// <summary>
        /// Checks if <paramref name="anchor"/> already has an existing member of
        /// this rule marker on the specified side (left/right) in the local frame.
        /// </summary>
        private static bool HasSideConnectionAtPoint(
            SG_Shape ss,
            Point3d anchor,
            Vector3d perp,
            bool wantRightSide,
            int ruleMarker)
        {
            foreach (var e in ss.Elems.OfType<SG_Elem1D>())
            {
                if (e.Autorule != ruleMarker) continue;
                if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null)
                    continue;

                bool anchorAt0 = e.Nodes[0].Pt.DistanceTo(anchor) < UT.PRES;
                bool anchorAt1 = e.Nodes[1].Pt.DistanceTo(anchor) < UT.PRES;
                if (!anchorAt0 && !anchorAt1) continue;

                var other = anchorAt0 ? e.Nodes[1].Pt : e.Nodes[0].Pt;
                double sign = (other - anchor) * perp;
                bool isRight = sign >= 0.0;
                if (isRight == wantRightSide) return true;
            }
            return false;
        }
    }
}
