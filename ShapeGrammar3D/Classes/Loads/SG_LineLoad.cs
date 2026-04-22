using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class SG_LineLoad : SG_Load
    {
        // --- properties ---
        public string ElementId{ get; set; }
        public int LoadCase { get; set; }
        public Vector3d Load { get; set; }

        // --- constructors ---
        public SG_LineLoad()
        {
            // empty
        }

        public SG_LineLoad(int _loadCase, Vector3d _loaddirection)
        {
            LoadCase = _loadCase;
            Load = _loaddirection;
        }

        // --- methods ---
        public override SG_Load DeepClone()
        {
            return new SG_LineLoad
            {
                ElementId = ElementId,
                LoadCase = LoadCase,
                Load = Load
            };
        }
    }
}
