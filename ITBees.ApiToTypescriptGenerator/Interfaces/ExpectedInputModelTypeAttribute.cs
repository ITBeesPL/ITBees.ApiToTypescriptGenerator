namespace ITBees.ApiToTypescriptGenerator.Interfaces;

public class ExpectedInputModelTypeAttribute : Attribute
{
    public Type Type { get; }
    public ExpectedInputModelTypeAttribute(Type type)
    {
        Type = type;
    }
}