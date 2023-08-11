using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ITBees.ApiToTypescriptGenerator.Interfaces;

namespace ITBees.ApiToTypescriptGenerator.Controllers;

public class AllControllersToTypescriptController : RestfulControllerBase<AllControllersToTypescriptController>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITypescriptGeneratorService _typescriptGeneratorService;

    public AllControllersToTypescriptController(ILogger<AllControllersToTypescriptController> logger,
        IServiceProvider serviceProvider, ITypescriptGeneratorService typescriptGeneratorService) : base(logger)
    {
        _serviceProvider = serviceProvider;
        _typescriptGeneratorService = typescriptGeneratorService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var result = _typescriptGeneratorService.GetAllControllersWithTypescriptModels();
        return Ok(result);
    }
}