using System;
using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;

namespace ShapeGrammar3D.Classes.Rules
{
    //[Serializable]
    //public class SH_Rule03 : SG_Rule
    //{
    //    // --- properties ---
    //    public double HorizontalThrustParameter { get; set; }
    //    public bool Compression { get; set; }
    //    private readonly double[] bounds = {0.1, 0.9};
    //    //public State RuleState = State.gamma;

    //    // --- constructors ---
    //    public SH_Rule03()
    //    {
    //        // empty
    //        RuleState = State.gamma;
    //        Name = "SH_Rule03";
    //    }
    //    public SH_Rule03(double _thrust, bool _compression)
    //    {
    //        Name = "SH_Rule03";
    //        RuleState = State.gamma;
    //        HorizontalThrustParameter = _thrust;
    //        Compression = _compression;
    //    }

    //    // --- methods ---
    //    public override SG_Rule CopyRule(SG_Rule rule)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public override string RuleOperation(ref SG_Shape _ss)
    //    {
    //        // test for correct state
    //        if (_ss.SimpleShapeState != State.gamma)
    //        {
    //            return "The current state is not compatible with Rule03.";
    //        }
    //        // --- solve ---
    //        SH_Evaluation.ConstructMatrices(_ss, out double[,] a, out double[] b);

            

    //        // calculate moments over the supports
    //        double[] moments = SH_Evaluation.CalculateMoments(a, b);
    //        double[] forces = SH_Evaluation.CalculateForces(_ss, moments);

    //        // calculate an estimated thrust
    //        double thrust = Math.Abs(forces.Sum() / 2) * HorizontalThrustParameter;

    //        //double thrust = 50; // make this a user specified input later.
    //        double[] reactions = SH_Evaluation.CalculateReactions(_ss, forces, thrust);

    //        // draw reciprocal diagram
    //        Dictionary<string, List<Line>> reciprocal_diagram; // = new Dictionary<string, List<Line>>();
    //        try
    //        {
    //            reciprocal_diagram = SH_Evaluation.DrawReciprocal(_ss, reactions, forces, thrust);
                
    //        }
    //        catch // (Exception ex)
    //        {
    //            throw new Exception("The number of elements are not sufficient to create the funicular. There must be at least two lines.");
    //            //AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Not enough elements to draw reciprocal. Minimum number is 2.");
    //        }

    //        // change state til "end" 
    //        _ss.SimpleShapeState = State.end;
    //        return "Rule03 successfully applied!";
    //    }

    //    public void SetLowerBound(double lowerBound)
    //    {
    //        this.bounds[0] = lowerBound;
    //    }
    //    public void SetUpperBound(double upperBound)
    //    {
    //        this.bounds[1] = upperBound;
    //    }
        
    //    public override void NewRuleParameters(Random random, SG_Shape ss)
    //    {
    //        HorizontalThrustParameter = UT.RandomExtensions.NextDouble(random, bounds[0], bounds[1]);            
    //    }

    //    public override State GetNextState()
    //    {
    //        return State.end;
    //    }
    //}
}
