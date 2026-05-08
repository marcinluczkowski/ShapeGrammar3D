using System;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// How a curve load is materialised when an Assembly resolves it
    /// against the elements of an SG_Shape.
    /// </summary>
    public enum CurveLoadDistribution
    {
        /// <summary>
        /// Distribute the uniform load along the curve as <see cref="SG_LineLoad"/>
        /// entries on every element the curve overlaps. Element keeps a single span;
        /// the FEM gets a per-segment line load equivalent.
        /// </summary>
        LineToBeams = 0,

        /// <summary>
        /// Sample the curve at <see cref="SG_CurveLoad.Subdivisions"/> stations,
        /// drop a mid-element node at each station's host beam and apply the
        /// tributary force as an <see cref="SG_PointLoad"/>. The element is NOT split.
        /// </summary>
        PointsOnNodes = 1
    }

    /// <summary>
    /// Uniform line/beam load defined along an arbitrary curve. The Assembly
    /// component snaps it to the elements it overlaps and turns it into either
    /// per-element line loads or per-mid-node point loads, depending on
    /// <see cref="Distribution"/>.
    /// </summary>
    [Serializable]
    public class SG_CurveLoad : SG_Load
    {
        /// <summary>Curve along which the load is applied (kept as NurbsCurve for serialisation).</summary>
        public NurbsCurve Curve { get; set; }

        /// <summary>Force per metre, in global XYZ. e.g. (0, 0, -10) = 10 kN/m downwards.</summary>
        public Vector3d Force { get; set; }

        /// <summary>How to distribute the load onto the assembled elements.</summary>
        public CurveLoadDistribution Distribution { get; set; } = CurveLoadDistribution.PointsOnNodes;

        /// <summary>Number of stations to sample along the curve when distribution is PointsOnNodes.</summary>
        public int Subdivisions { get; set; } = 10;

        public int LoadCase { get; set; } = 0;

        public SG_CurveLoad() { }

        public SG_CurveLoad(NurbsCurve curve, Vector3d force, CurveLoadDistribution dist, int subdivisions, int loadCase = 0)
        {
            Curve = curve;
            Force = force;
            Distribution = dist;
            Subdivisions = Math.Max(2, subdivisions);
            LoadCase = loadCase;
        }

        public override SG_Load DeepClone()
        {
            return new SG_CurveLoad
            {
                Curve = Curve?.DuplicateCurve() as NurbsCurve,
                Force = Force,
                Distribution = Distribution,
                Subdivisions = Subdivisions,
                LoadCase = LoadCase
            };
        }
    }
}
