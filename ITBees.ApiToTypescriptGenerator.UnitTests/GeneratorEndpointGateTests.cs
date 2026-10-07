using ITBees.ApiToTypescriptGenerator.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace ITBees.ApiToTypescriptGenerator.UnitTests;

[TestFixture]
public class GeneratorEndpointGateTests
{
    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IActionResult? RunGate(string environmentName, string? configuredValue = null,
        bool? configuredInCode = null)
    {
        var configuration = new Dictionary<string, string?>();
        if (configuredValue != null)
            configuration["ApiToTypescriptGenerator:EndpointsEnabled"] = configuredValue;

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        if (configuredInCode != null)
            services.Configure<ApiToTypescriptGeneratorOptions>(x => x.EndpointsEnabled = configuredInCode);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var context = new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new List<IValueProviderFactory>());

        new GeneratorEndpointGateAttribute().OnResourceExecuting(context);
        return context.Result;
    }

    [Test]
    public void Development_without_configuration_lets_the_request_through()
    {
        Assert.That(RunGate("Development"), Is.Null);
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    [TestCase("dev")]
    public void Other_environments_without_configuration_answer_404(string environmentName)
    {
        Assert.That(RunGate(environmentName), Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Configuration_can_enable_the_endpoints_outside_Development()
    {
        Assert.That(RunGate("dev", configuredValue: "true"), Is.Null);
    }

    [Test]
    public void Configuration_can_disable_the_endpoints_in_Development()
    {
        Assert.That(RunGate("Development", configuredValue: "false"), Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Options_set_in_code_win_over_configuration()
    {
        Assert.That(RunGate("Production", configuredValue: "true", configuredInCode: false),
            Is.InstanceOf<NotFoundResult>());
        Assert.That(RunGate("Production", configuredValue: "false", configuredInCode: true), Is.Null);
    }

    [Test]
    public void Every_controller_of_the_library_is_gated()
    {
        var controllers = typeof(GeneratorEndpointGateAttribute).Assembly.GetTypes()
            .Where(x => typeof(ControllerBase).IsAssignableFrom(x) && !x.IsAbstract)
            .ToList();

        Assert.That(controllers, Is.Not.Empty);
        Assert.That(controllers.Where(x => !x.IsDefined(typeof(GeneratorEndpointGateAttribute), true)),
            Is.Empty);
    }
}
