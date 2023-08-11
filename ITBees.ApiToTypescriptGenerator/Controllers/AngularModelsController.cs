using ITBees.ApiToTypescriptGenerator.Interfaces;
using ITBees.Interfaces.Platforms;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.ApiToTypescriptGenerator.Controllers;

public class AngularModelsController : RestfulControllerBase<AngularModelsController>
{
    private readonly ITypescriptGeneratorService _generatorService;
    private readonly IPlatformSettingsService _platformSettingsService;

    public AngularModelsController(
        ILogger<AngularModelsController> logger, 
        ITypescriptGeneratorService generatorService, 
        IPlatformSettingsService platformSettingsService
        ) : base(logger)
    {
        _generatorService = generatorService;
        _platformSettingsService = platformSettingsService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var result = _generatorService.GetAllControllersWithTypescriptModels();
        var platfromName = _platformSettingsService.GetSetting("PlatformName");
        return File(result.ZipArchive, "application/zip", $"{platfromName}_models_{DateTime.Now.ToString("yyyyMMdd")}.zip");
    }
}