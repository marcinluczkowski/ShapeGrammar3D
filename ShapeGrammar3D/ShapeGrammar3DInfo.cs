using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace ShapeGrammar3D
{
    public class ShapeGrammar3DInfo : GH_AssemblyInfo
    {
        public override string Name => "ShapeGrammar3D";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => global::ShapeGrammar3D.Properties.Resources.icons_Generic;

        //Return a short string describing the purpose of this GHA library.
        public override string Description =>
            "Shape Grammar 3D: assembly, loads, rules, GA interpreters, and structural previews for Grasshopper.";

        public override Guid Id => new Guid("83244308-313c-44b4-8ab8-af98a582266f");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}