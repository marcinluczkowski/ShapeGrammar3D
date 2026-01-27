using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShapeGrammar3D.Classes.Elements;
using Rhino.Geometry;


namespace ShapeGrammar3D.Classes.Rules
{
    [Serializable]
    public class SH_Rule01 : SG_Rule
    {
        // --- properties ---
        public Vector3d TranslateStart { get; private set; }
        public Vector3d TranslateEnd { get; private set; }
        

        private double[] xBounds = { -1.0, 1.0 }; // lower and upper bounds for translation of of support in x direction
        private double[] yBounds = { -1.0, 1.0 }; // lower and upper bounds for translation of of support in y direction
        private double[] zBounds = { -3.0, 3.0 }; // lower and upper bounds for translation of of support in z direction

        //public State RuleState = State.alpha;

        // --- constructors ---
        public SH_Rule01()
        {
            // empty constructor
            RuleState = State.alpha;
            Name = "SH_Rule01";
        }
        public SH_Rule01(Vector3d _translate_start, Vector3d _translate_end)
        {
            TranslateStart = _translate_start;
            TranslateEnd = _translate_end;
            RuleState = State.alpha;
            Name = "SH_Rule01";
        }

        // --- methods ---
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        public override string RuleOperation(ref SG_Shape _ss)
        {
            // check if the state matches the simple shape state
            if (_ss.SimpleShapeState != RuleState)
            {
                return "The State is not compatible with Rule01.";
            }

            // take the 1st element
            // SH_Line sh_elem = (SH_Line) _ss.Elements["Line"][0];
            SG_Elem1D sh_elem = (SG_Elem1D)_ss.Elems[0];

            // apply the change
            #region NewMethod
            // new points
            //Point3d newStart = sh_elem.Nodes[0].Position + TranslateStart;
            //Point3d newEnd = sh_elem.Nodes[1].Position + TranslateEnd;            
            _ss.TranslateNode(TranslateStart, (int)sh_elem.Nodes[0].ID);
            _ss.TranslateNode(TranslateEnd, (int)sh_elem.Nodes[1].ID);

            #endregion

            #region Original Method

            /*
            Point3d currentStart = sh_line.Nodes[0].Position;
            Point3d currentEnd = sh_line.Nodes[1].Position;
            
            sh_line.Nodes[0].Position = currentStart + TranslateStart;
            sh_line.Nodes[1].Position = currentEnd + TranslateEnd;
            */

            #endregion
            // change the state
            _ss.SimpleShapeState = State.beta;
            return "Rule01 successfully applied.";

        }

        /// <summary>
        /// Set the lower bounds of translation vectors
        /// </summary>
        /// <param name="xLow"></param>
        /// <param name="yLow"></param>
        /// <param name="zLow"></param>
        public void SetLowerBounds(double xLow, double yLow, double zLow)
        {
            xBounds[0] = xLow;
            yBounds[0] = yLow;
            zBounds[0] = zLow;
        }
        /// <summary>
        /// Set the upper bounds of the translation vectors
        /// </summary>
        /// <param name="xHigh"></param>
        /// <param name="yHigh"></param>
        /// <param name="zHigh"></param>
        public void SetUpperBounds(double xHigh, double yHigh, double zHigh)
        {
            xBounds[1] = xHigh;
            yBounds[1] = yHigh;
            zBounds[1] = zHigh;
        }

        public override void NewRuleParameters(Random random, SG_Shape ss)
        {
            // create a random parameter
            double x0 = UT.RandomExtensions.NextDouble(random, xBounds[0], xBounds[1]);
            double x1 = UT.RandomExtensions.NextDouble(random, xBounds[0], xBounds[1]);
            double y0 = UT.RandomExtensions.NextDouble(random, yBounds[0], yBounds[1]);
            double y1 = UT.RandomExtensions.NextDouble(random, yBounds[0], yBounds[1]);
            double z0 = UT.RandomExtensions.NextDouble(random, zBounds[0], zBounds[1]);
            double z1 = UT.RandomExtensions.NextDouble(random, zBounds[0], zBounds[1]);
            
            // set the vectors
            TranslateStart = new Vector3d(x0, y0, z0);
            TranslateEnd = new Vector3d(x1, y1, z1);
        }

        public override State GetNextState()
        {
            return State.beta;
        }
    }
}
