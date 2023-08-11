using ITBees.ApiToTypescriptGenerator.Services;
using ITBees.ApiToTypescriptGenerator.Services.Models;

namespace ITBees.ApiToTypescriptGenerator.Interfaces;

public interface ITypescriptGeneratorService
{
    AllTypescriptModels GetAllControllersWithTypescriptModels();
}