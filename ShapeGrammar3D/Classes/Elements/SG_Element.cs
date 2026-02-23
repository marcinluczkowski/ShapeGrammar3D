using System;
using System.Text.Json.Serialization;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Classes.Elements
{
    [Serializable]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(SG_Elem1D), typeDiscriminator: "elem1d")]
    public abstract class SG_Element : IDeepCloneable<SG_Element>
    {
        protected SG_Element() { }

        // --- properties ---
        public int ID { get; set; }
        public string Name { get; set; }
        public int Autorule { get; set; }
        public SG_Node[] Nodes { get; set; }

        public abstract SG_Element DeepClone();

        private static SG_Shape CloneShape(SG_Shape source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source.DeepCopy();
        }
    }
}
