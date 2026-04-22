using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Shared helpers for rule061/062/063/064. Responsible for finding the
    /// InitShape base beam associated with each RULE020 strut, grouping struts by
    /// base beam, and deduplicating new members.
    /// </summary>
    internal static class Rule06xHelper
    {
        /// <summary>
        /// One strut (RULE020 element) with its associated InitShape base curve
        /// and the parameter of its foot on that curve.
        /// </summary>
        public sealed class StudBaseInfo
        {
            public SG_Elem1D Stud;
            public Curve BaseCurve;
            public double FootT;
        }

        /// <summary>
        /// Locates the RULE010 (InitShape) base beam attached to the foot of a
        /// strut and returns its Joined_Init_Crv. Returns null if no such beam
        /// is found or if it has no joined curve.
        /// </summary>
        public static Curve FindBaseCurve(SG_Elem1D stud)
        {
            if (stud == null || stud.Nodes == null || stud.Nodes.Length < 2 || stud.Nodes[0] == null)
                return null;
            var baseElem = stud.Nodes[0].Elements
                .OfType<SG_Elem1D>()
                .FirstOrDefault(e => e.Autorule == UT.RULE010_MARKER);
            return baseElem?.Joined_Init_Crv;
        }

        /// <summary>
        /// Collects all RULE020 struts that have a valid InitShape base curve,
        /// together with the param of their foot on that curve.
        /// </summary>
        public static List<StudBaseInfo> CollectStudBaseInfos(SG_Shape shape)
        {
            var list = new List<StudBaseInfo>();
            if (shape?.Elems == null) return list;

            foreach (var se in shape.Elems.OfType<SG_Elem1D>())
            {
                if (se.Name != "3DAR2") continue;
                if (se.Nodes == null || se.Nodes.Length < 2 || se.Nodes[0] == null || se.Nodes[1] == null)
                    continue;
                var baseCrv = FindBaseCurve(se);
                if (baseCrv == null) continue;
                if (!baseCrv.ClosestPoint(se.Nodes[0].Pt, out double t)) continue;
                list.Add(new StudBaseInfo { Stud = se, BaseCurve = baseCrv, FootT = t });
            }
            return list;
        }

        /// <summary>
        /// Groups the given stud infos by their base curve (reference equality)
        /// and sorts each group in ascending foot param so consecutive entries
        /// are physically adjacent along the base beam.
        /// </summary>
        public static Dictionary<Curve, List<StudBaseInfo>> GroupByBaseCurve(IEnumerable<StudBaseInfo> infos)
        {
            var dict = new Dictionary<Curve, List<StudBaseInfo>>(CurveReferenceComparer.Instance);
            foreach (var info in infos)
            {
                if (!dict.TryGetValue(info.BaseCurve, out var lst))
                {
                    lst = new List<StudBaseInfo>();
                    dict[info.BaseCurve] = lst;
                }
                lst.Add(info);
            }
            foreach (var lst in dict.Values)
                lst.Sort((a, b) => a.FootT.CompareTo(b.FootT));
            return dict;
        }

        /// <summary>
        /// True if an existing SG_Elem1D already spans the two endpoints of
        /// <paramref name="newLn"/> (within tolerance, either direction).
        /// </summary>
        public static bool LineAlreadyPresent(SG_Shape shape, Line newLn)
        {
            foreach (var e in shape.Elems.OfType<SG_Elem1D>())
            {
                if (e.Nodes == null || e.Nodes.Length < 2 || e.Nodes[0] == null || e.Nodes[1] == null)
                    continue;
                var p0 = e.Nodes[0].Pt;
                var p1 = e.Nodes[1].Pt;
                bool fwd = newLn.From.DistanceTo(p0) < UT.PRES && newLn.To.DistanceTo(p1) < UT.PRES;
                bool rev = newLn.From.DistanceTo(p1) < UT.PRES && newLn.To.DistanceTo(p0) < UT.PRES;
                if (fwd || rev) return true;
            }
            return false;
        }

        /// <summary>
        /// Canonical unordered-pair key for two base curves (reference equality).
        /// <c>.First</c> always has a hash &lt;= <c>.Second</c>.
        /// </summary>
        public readonly struct CurvePairKey : IEquatable<CurvePairKey>
        {
            public Curve First { get; }
            public Curve Second { get; }

            public CurvePairKey(Curve a, Curve b)
            {
                var ha = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
                var hb = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b);
                if (ha <= hb)
                {
                    First = a;
                    Second = b;
                }
                else
                {
                    First = b;
                    Second = a;
                }
            }

            public bool Equals(CurvePairKey other)
                => ReferenceEquals(First, other.First) && ReferenceEquals(Second, other.Second);

            public override bool Equals(object obj)
                => obj is CurvePairKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h1 = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(First);
                    int h2 = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Second);
                    return h1 * 397 ^ h2;
                }
            }
        }

        public sealed class CurveReferenceComparer : IEqualityComparer<Curve>
        {
            public static readonly CurveReferenceComparer Instance = new CurveReferenceComparer();
            public bool Equals(Curve x, Curve y) => ReferenceEquals(x, y);
            public int GetHashCode(Curve obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
