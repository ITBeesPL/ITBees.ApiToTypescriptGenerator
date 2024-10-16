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

                // Try to get the Type from the provided viewModelName
                Type type = Type.GetType(viewModelName);
                if (type == null)
                {
                    // If Type.GetType() didn't find it, search all loaded assemblies
                    type = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.Name == viewModelName || t.FullName == viewModelName);
                }

                if (type == null)
                {
                    return BadRequest($"Type '{viewModelName}' not found.");
                }

                var result = typeScriptGenerator.Generate(type, new TypeScriptGeneratedModels(), false);
                return Ok(new TypeScriptModelsVm() { Models = result.ToString() });
            }
            catch (Exception e)
            {
                return CreateBaseErrorResponse(e, viewModelName);
            }
        }
    }

}