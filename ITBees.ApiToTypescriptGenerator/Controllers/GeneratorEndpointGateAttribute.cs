using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ITBees.ApiToTypescriptGenerator.Controllers;

/// <summary>
/// Answers 404 unless the generator endpoints are enabled, see <see cref="ApiToTypescriptGeneratorOptions"/>.
/// It is a resource filter, so a disabled endpoint returns before model binding and before any generation work.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class GeneratorEndpointGateAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!AreEndpointsEnabled(context.HttpContext.RequestServices))
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    internal static bool AreEndpointsEnabled(IServiceProvider services)
    {
        var configuredInCode = services.GetService<IOptions<ApiToTypescriptGeneratorOptions>>()?.Value.EndpointsEnabled;
        var configured = services.GetService<IConfiguration>()?
            .GetValue<bool?>($"{ApiToTypescriptGeneratorOptions.ConfigurationSection}:{nameof(ApiToTypescriptGeneratorOptions.EndpointsEnabled)}");

        return configuredInCode
               ?? configured
               ?? services.GetService<IHostEnvironment>()?.IsDevelopment() == true;
    }
}
