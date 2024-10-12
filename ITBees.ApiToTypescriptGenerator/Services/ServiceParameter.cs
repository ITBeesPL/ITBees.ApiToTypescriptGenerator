public class ServiceParameter
{
    public string Name { get; set; }
    public Type ParameterType { get; set; }
    public bool FromBody { get; set; }
    public bool FromQuery { get; set; }
    public bool FromRoute { get; set; }
}