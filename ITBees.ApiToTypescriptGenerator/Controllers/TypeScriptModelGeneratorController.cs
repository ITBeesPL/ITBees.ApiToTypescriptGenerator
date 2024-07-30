using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.ApiToTypescriptGenerator.Controllers
{

    //[ApiExplorerSettings(IgnoreApi = true)]
    public class TypeScriptModelGeneratorController : RestfulControllerBase<TypeScriptModelGeneratorController>
    {
        private readonly ILogger<TypeScriptModelGeneratorController> _logger;

        public TypeScriptModelGeneratorController(
            ILogger<TypeScriptModelGeneratorController> logger) : base(logger)
        {
            _logger = logger;
        }

        [Produces(typeof(TypeScriptModelsVm))]
        [HttpGet]
        public ActionResult<string> Get(string viewModelName)
        {
            try
            {
                var typeScriptGenerator = new TypeScriptGenerator();
                var result = typeScriptGenerator.Generate(viewModelName, new TypeScriptGeneratedModels());
                return Ok(new TypeScriptModelsVm() { Models = result.ToString() });
            }
            catch (Exception e)
            {
                return CreateBaseErrorResponse(e, viewModelName);
            }
        }
    }
}