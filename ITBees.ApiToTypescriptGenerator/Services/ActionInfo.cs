namespace ITBees.ApiToTypescriptGenerator.Services;

public class ActionInfo
{
    public string ActionName { get; set; }
    public string HttpMethod { get; set; }
    public Type ReturnType { get; set; }
    public Type ParameterType { get; set; }
}