public class ServiceMethod
{
    public string ControllerName { get; set; }
    public string ActionName { get; set; }
    public string HttpMethod { get; set; } // GET, POST, PUT, DELETE
    public List<ServiceParameter> Parameters { get; set; }
    public Type ReturnType { get; set; }
}