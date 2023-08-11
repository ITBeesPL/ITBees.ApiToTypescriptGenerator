using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text;

namespace ITBees.ApiToTypescriptGenerator.Controllers;

public class AllControllersToTypescriptController : RestfulControllerBase<AllControllersToTypescriptController>
{
    private readonly IServiceProvider _serviceProvider;

    public AllControllersToTypescriptController(ILogger<AllControllersToTypescriptController> logger,
        IServiceProvider serviceProvider) : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Produces(typeof(List<TypescriptControllerVm>))]
    public IActionResult Get()
    {
        var partManager = _serviceProvider.GetRequiredService<ApplicationPartManager>();

        var feature = new ControllerFeature();
        var controllers = new List<TypescriptControllerVm>();
        partManager.PopulateFeature(feature);

        var actionDescriptorCollectionProvider = _serviceProvider.GetRequiredService<IActionDescriptorCollectionProvider>();
        var sb = new StringBuilder();
        var typeScriptGenerator = new TypeScriptGenerator();

        foreach (var actionDescriptor in actionDescriptorCollectionProvider.ActionDescriptors.Items)
        {
            if (actionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
            {
                sb.AppendLine($"[Controller /{controllerActionDescriptor.ControllerName}]");
                sb.AppendLine($"Action: {controllerActionDescriptor.ActionName}");


                var producesAttribute = controllerActionDescriptor.MethodInfo.GetCustomAttribute<ProducesAttribute>();
                sb.AppendLine("\tProduces :");
                if (producesAttribute != null && producesAttribute.Type != null)
                {

                    sb.AppendLine($"\tProduces Type: {producesAttribute.Type.Name}");
                    sb.AppendLine("***\r\n" + typeScriptGenerator.Generate(producesAttribute.Type.Name, new TypeScriptGeneratedModels()) + "***\r\n");
                }
                sb.AppendLine($"[Controller /{controllerActionDescriptor.ControllerName} request type : {controllerActionDescriptor.ActionName}]");
                foreach (var parameter in controllerActionDescriptor.Parameters)
                {
                    sb.AppendLine($"\t\tParameter: {parameter.Name}, Type: {parameter.ParameterType}");
                }
            }
        }

        return Ok(sb.ToString());
    }
}

public class TypescriptControllerVm
{
    public string? ControllerFullName { get; }
    public string Action { get; }
    public string? Template { get; }

    public TypescriptControllerVm(string? controllerFullName)
    {
        ControllerFullName = controllerFullName;
    }

    public TypescriptControllerVm(string controllerFullName, string action, string? template)
    {
        ControllerFullName = controllerFullName;
        Action = action;
        Template = template;
    }
}