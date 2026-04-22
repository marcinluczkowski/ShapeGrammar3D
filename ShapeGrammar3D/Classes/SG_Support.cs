using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class SG_Support : IDeepCloneable<SG_Support>
    {
        // --- properties ---
        
        public int ID;
        // public int nodeInd;
        public SG_Node Node { get; set; }
        public Point3d Pt { get; set; }
        public int SupportCondition { get; set; }

        // --- constructors ---

        public SG_Support()
        {

        }
        public SG_Support(string _sup_cond_txt, Point3d _pt) //  SG_Node _node
        {
            // Test if the support condition are in the correct format
            if (_sup_cond_txt.Length != 6) throw new Exception("The length of the string must be exactly 6 characters");

            //Test if the elements in the string are correct
            foreach (char item in _sup_cond_txt)
            {
                if (item != '0' && item != '1')
                {
                    throw new Exception("Only 0 and 1 should be present in the string");
                }
            }

            // Create the support condition 
            SupportCondition = SetConditions(_sup_cond_txt);
            Pt = _pt;
            
        }
        // --- methods ---
        private int SetConditions(string _stringCondition)
        {
            int condition = 0;
            int n = 0;
            foreach (char el in _stringCondition)
            {
                if (el == '1')
                {
                    condition += (int) Math.Pow(2, n);
                }
                n++; 
            }
            return condition;            
        }

        public List<bool> GetBoolConditions()
        {
            var boolConditions = new List<bool>(6);
            uint cond = (uint)SupportCondition;
            for (int i = 0; i < 6; i++)
            {
                boolConditions.Add(((cond >> i) & 1u) != 0u);
            }
            return boolConditions;
        }

        public SG_Support DeepClone()
        {
            return new SG_Support
            {
                ID = ID,
                Pt = Pt,
                SupportCondition = SupportCondition,
                Node = null
            };
        }

    }
}
