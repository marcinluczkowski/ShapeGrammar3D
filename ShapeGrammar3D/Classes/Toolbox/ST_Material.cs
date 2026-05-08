using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace ShapeGrammar3D.Classes.Toolbox
{
    [Serializable]
    public class TB_Material
    {
        // --- field ---
        public string Tag { get; private set; } = "N/A";
        public double E { get; private set; }
        public double G { get; private set; }
        public double Gamma { get; private set; }
        public double Alpha { get; private set; }
        public double Fy { get; private set; }

        // --- constructors --- 
        public TB_Material() { }

        public TB_Material(string _tag, double _E, double _G, double _gamma, double _alpha, double _Fy)
        {
            Tag = _tag;
            E = _E;
            G = _G;
            Gamma = _gamma;
            Alpha = _alpha;
            Fy = _Fy;

        }


        // --- methods ---
        public TB_Material DeepCopy()
        {
            return (TB_Material)base.MemberwiseClone();
        }
        public override string ToString()
        {
            string txt = "Material, " + Tag;
            return txt;
        }
        public bool IsValid()
        {
            return Tag != "N/A";
        }

    }

    public class GH_Material : GH_Goo<TB_Material>
    {
        public GH_Material() { }
        public GH_Material(GH_Material other) : base(other.Value)
        {
            this.Value = other.Value.DeepCopy();
        }
        public GH_Material(TB_Material mat) : base(mat)
        {
            this.Value = mat;
        }
        public override bool IsValid => base.m_value.IsValid();
        public override string TypeName => "Material";
        public override string TypeDescription => "Material";
        public override IGH_Goo Duplicate()
        {
            return new GH_Material(this);
        }
        public override string ToString()
        {
            return Value.ToString();
        }
    }

    public class Param_Material : GH_PersistentParam<GH_Material>
    {
        public Param_Material() : base(
            new GH_InstanceDescription(
                "Material", "Mat", "Isotropic steel-like material (E, G, density, yield) for sections.", Common.category, Common.sub_param
                )
            )
        { }

        public override Guid ComponentGuid => new Guid("91bbf942-086c-4470-82c8-7c7bb78b841f");

        protected override System.Drawing.Bitmap Icon { get {
                return global::ShapeGrammar3D.Properties.Resources.icons_Generic;
            } }  //Set icon image

        protected override GH_GetterResult Prompt_Plural(ref List<GH_Material> values)
        {
            return GH_GetterResult.success;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_Material value)
        {
            return GH_GetterResult.success;
        }


    }


}
