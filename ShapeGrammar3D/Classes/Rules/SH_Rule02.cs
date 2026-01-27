using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;
using ShapeGrammar3D.Classes.Elements;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Classes.Rules
{
    /// <summary>
    /// Rule splitting the line at the input parameter
    /// </summary>
    [Serializable]
    public class SH_Rule02 : SG_Rule
    {
        // --- properties ---

        public int LineIndex { get; set; }
        public double Param { get; set; }
        private readonly double[] bounds  = { 0.2, 0.8 };


        // --- constructors ---
        public SH_Rule02()
        {
            RuleState = State.beta;
            Name = "SH_Rule02";
        }

        public SH_Rule02(int _line_index, double _param)
        {
            Name = "SH_Rule02";
            LineIndex = _line_index;
            Param = _param;
            RuleState = State.beta;            
        }

        // --- methods ---
        public override SG_Rule CopyRule(SG_Rule rule)
        {
            throw new NotImplementedException();
        }

        public override string RuleOperation(ref SG_Shape _ss)
        {

            // check if the state maches the simple shape state
            if (_ss.SimpleShapeState != RuleState)
            {
                return "The current state is not compatible with Rule02.";
            }

            // choose the line to split
            //SH_Element line = _ss.Lines.Where(l => l.ID == LineID).First(); DELETE IF OK
            SG_Elem1D line = new SG_Elem1D();
            // to do: evaluate if this is the best method for avoiding an index out of range error. 
            try
            {
                line = (SG_Elem1D)_ss.Elems[LineIndex];
            }
            catch (Exception ex)
            {
                if (ex is ArgumentOutOfRangeException || ex is IndexOutOfRangeException)
                {
                   line = (SG_Elem1D)_ss.Elems[_ss.elementCount-1]; // if out of range, take the last item
                }
                
            }
             
            double line_length = line.Nodes[0].Pt.DistanceTo(line.Nodes[1].Pt);
            double segment1 = line_length * Param;
            double segment2 = line_length * (1 - Param);
            // test if the line original length or the splitted segments are smaller than 1 meter. If true the rule cannot be applied. 
            if ((line_length < 1.0) || (segment1 < 1.0) || (segment2 < 1.0))
            {
                return "The line segment are too short for the rule to be applied on this element"; 
            }

            int elInd = _ss.Elems.IndexOf(line);
            // add the intermediate node
            SG_Node newNode = AddNode(line, Param, _ss.nodeCount);
            _ss.Nodes.Add(newNode);
            _ss.nodeCount++;

            // create 2x lines 

            SG_Elem1D newLine0 = new SG_Elem1D(new SG_Node[] { line.Nodes[0], newNode }, line.ID, line.Name); // add the element name here too.
            //_ss.elementCount++;  DELETE if above method is working
            SG_Elem1D newLine1 = new SG_Elem1D(new SG_Node[] { newNode, line.Nodes[1] }, _ss.elementCount, line.Name);
            _ss.elementCount++;
            
            // remove the line which has been split
            _ss.Elems.RemoveAt(elInd);
            _ss.Elems.Insert(elInd, newLine1);

            // insert the new lines in its position
            _ss.Elems.Insert(elInd, newLine0);

            // no change in the state (remains in beta state)
            // _ss.SimpleShapeState = State.beta; 

            return "Rule02 successfully applied!";

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="_line"> The line to operate on</param>
        /// <param name="_t"> Splitting parameter</param>
        /// <param name="_id">ID of the line</param>
        /// <returns></returns>
        private SG_Node AddNode(SG_Element _line, double _t, int _id)
        {
            double mx = (1 - _t) * _line.Nodes[0].Pt.X + _t * _line.Nodes[1].Pt.X;
            double my = (1 - _t) * _line.Nodes[0].Pt.Y + _t * _line.Nodes[1].Pt.Y;
            double mz = (1 - _t) * _line.Nodes[0].Pt.Z + _t * _line.Nodes[1].Pt.Z;
            Point3d newPoint = new Point3d(mx, my, mz);
            SG_Node newNode = new SG_Node(newPoint, _id);

            return newNode;
        }
        /// <summary>
        /// Set the lower bound of the split parameter
        /// </summary>
        /// <param name="lower"></param>
        public void SetLowerBound(double lower)
        {
            bounds[0] = lower;
        }
        /// <summary>
        /// Gets the lower bound of the split parameter
        /// </summary>
        /// <returns> lower bound </returns>
        public double GetLowerBound()
        {
            return bounds[0];
        }
        /// <summary>
        /// Set the upper bound of the split parameter
        /// </summary>
        /// <param name="upper"></param>
        public void SetUpperBound(double upper)
        {
            bounds[1] = upper;
        }
        /// <summary>
        /// Gets the upper bound of the split parameter
        /// </summary>
        /// <returns>Upper bound</returns>
        public double GetUpperBound()
        {
            return bounds[1];
        }

        public override void NewRuleParameters(Random random, SG_Shape ss)
        {
            // the parameter to use for the rule
            int numLines = ss.elementCount;
            Param = UT.RandomExtensions.NextDouble(random, bounds[0], bounds[1]);
            LineIndex = random.Next(0, numLines-1);
            
        }

        public override State GetNextState()
        {
            return State.beta;
        }
    }
}
