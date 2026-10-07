using System.IO.Compression;
using System.Net;
using System.Text.Json;
using ITBees.ApiToTypescriptGenerator.Controllers;
using ITBees.ApiToTypescriptGenerator.Interfaces;
using ITBees.ApiToTypescriptGenerator.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ITBees.ApiToTypescriptGenerator.UnitTests;

/// <summary>An app action returning an entity with a reference cycle, like TimBe's legacy /api controllers.</summary>
[Route("[controller]")]
public class CyclicEntityController : ControllerBase
{
    [HttpGet]
    public ActionResult<TypeScriptGeneratorCycleTests.UserAccount> Get() => new TypeScriptGeneratorCycleTests.UserAccount();
}

/// <summary>GET /AllControllersToTypescript the way fas-gen calls it: anonymous, against the running app.</summary>
[TestFixture]
public class AllControllersToTypescriptEndpointTests
{
    private static async Task<HttpResponseMessage> GetAllControllersToTypescript(string environmentName,
        params KeyValuePair<string, string?>[] configuration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new AssemblyPart(typeof(AllControllersToTypescriptController).Assembly));
            manager.ApplicationParts.Add(new AssemblyPart(typeof(CyclicEntityController).Assembly));
        });
        builder.Services.AddScoped<ITypescriptGeneratorService, TypescriptGeneratorService>();

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        return await app.GetTestClient().GetAsync("/AllControllersToTypescript");
    }

    [Test]
    public async Task Development_generates_models_for_an_action_returning_a_cyclic_entity()
    {
        var response = await GetAllControllersToTypescript("Development");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var zip = new ZipArchive(new MemoryStream(json.RootElement.GetProperty("zipArchive").GetBytesFromBase64()));
        var userAccount = zip.GetEntry("user-account.model.ts");
        Assert.That(userAccount, Is.Not.Null);
        using var reader = new StreamReader(userAccount!.Open());
        Assert.That(await reader.ReadToEndAsync(), Does.Contain("partnerAccount?: IUserAccount;"));
    }

    [Test]
    public async Task Production_answers_404()
    {
        var response = await GetAllControllersToTypescript("Production");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Production_with_the_endpoints_enabled_in_configuration_answers()
    {
        var response = await GetAllControllersToTypescript("Production",
            new KeyValuePair<string, string?>("ApiToTypescriptGenerator:EndpointsEnabled", "true"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
