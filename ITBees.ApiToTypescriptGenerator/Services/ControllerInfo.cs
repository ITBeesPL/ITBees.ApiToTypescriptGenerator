namespace ITBees.ApiToTypescriptGenerator.Services;

public class ControllerInfo
{
    public string ControllerName { get; set; }
    public string EndpointName { get; set; }
    public List<ActionInfo> Actions { get; set; } = new List<ActionInfo>();
}