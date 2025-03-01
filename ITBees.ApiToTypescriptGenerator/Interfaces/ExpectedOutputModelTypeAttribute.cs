namespace ITBees.ApiToTypescriptGenerator.Interfaces;

public class ExpectedOutputModelTypeAttribute : Attribute
{
    public Type Type { get; }
    public ExpectedOutputModelTypeAttribute(Type type)
    {
        Type = type;
    }
}