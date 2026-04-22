namespace ShapeGrammar3D.Classes
{
    public interface IDeepCloneable<out T>
    {
        T DeepClone();
    }
}