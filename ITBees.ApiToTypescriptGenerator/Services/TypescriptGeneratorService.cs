using System.IO.Compression;
using System.Reflection;
using System.Text;
using ITBees.ApiToTypescriptGenerator.Interfaces;
using ITBees.ApiToTypescriptGenerator.Services.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.ApiToTypescriptGenerator.Services;

public class TypescriptGeneratorService : ITypescriptGeneratorService
{
    private readonly IServiceProvider _serviceProvider;

    public TypescriptGeneratorService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    /// <summary>
    /// Returns all registeres controllers with their input parameters, methods, and returning types as typescript interfaces returned as long string
    /// </summary>
    /// <returns></returns>
    public AllTypescriptModels GetAllControllersWithTypescriptModels()
    {
        var partManager = _serviceProvider.GetRequiredService<ApplicationPartManager>();
        var feature = new ControllerFeature();
        partManager.PopulateFeature(feature);
        var actionDescriptorCollectionProvider = _serviceProvider.GetRequiredService<IActionDescriptorCollectionProvider>();
        var sb = new StringBuilder();
        var typeScriptGenerator = new TypeScriptGenerator();
        var generatedTypescriptModels = new Dictionary<string, TypeScriptFile>();
        byte[] zipBytes;

        using (var zipMemoryStream = new MemoryStream())
        {
            using (ZipArchive zipArchive = new ZipArchive(zipMemoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var actionDescriptor in actionDescriptorCollectionProvider.ActionDescriptors.Items)
                {
                    if (actionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                    {
                        var typescriptFolder = controllerActionDescriptor.ControllerName;
                        sb.AppendLine($"[Controller /{controllerActionDescriptor.ControllerName}]");
                        sb.AppendLine($"Action: {controllerActionDescriptor.ActionName}");

                        // Handle ProducesAttribute
                        var producesAttribute = controllerActionDescriptor.MethodInfo.GetCustomAttribute<ProducesAttribute>();
                        sb.AppendLine("\tProduces :");
                        if (producesAttribute != null && producesAttribute.Type != null)
                        {
                            var producedType = producesAttribute.Type;

                            TypeScriptGeneratedModels typeScriptGeneratedModels = null;
                            TypeScriptFile typeScriptGeneratedModel = null;
                            if (producedType.IsGenericType && producedType.GetGenericTypeDefinition() == typeof(List<>))
                            {
                                var itemType = producedType.GetGenericArguments()[0];
                                sb.AppendLine($"\tProduces Type: List<{itemType.Name}>");

                                if (CheckOutputTypeIsHandledByGeneratr(itemType, sb))
                                {
                                    typeScriptGeneratedModels = typeScriptGenerator.Generate(itemType.Name, new TypeScriptGeneratedModels(), false);
                                    typeScriptGeneratedModel = new TypeScriptFile(
                                        typeScriptGeneratedModels.ToString(),
                                        itemType.Name);
                                }
                            }
                            else
                            {
                                sb.AppendLine($"\tProduces Type: {producedType.Name}");
                                if (CheckOutputTypeIsHandledByGeneratr(producedType, sb))
                                {
                                    typeScriptGeneratedModels = typeScriptGenerator.Generate(producedType.Name, new TypeScriptGeneratedModels(), true);

                                    typeScriptGeneratedModel = new TypeScriptFile(
                                        typeScriptGeneratedModels.ToString(),
                                        producedType.Name);
                                }
                            }

                            if (typeScriptGeneratedModel != null)
                            {
                                generatedTypescriptModels.TryAdd(typeScriptGeneratedModel.TypeName,
                                    typeScriptGeneratedModel);
                                sb.AppendLine("***\r\n" + typeScriptGeneratedModel + "***\r\n");
                                foreach (var typescriptModel in typeScriptGeneratedModels.GeneratedOjects)
                                {
                                    if (generatedTypescriptModels.ContainsKey(typescriptModel.ClassType) == false)
                                    {
                                        generatedTypescriptModels.TryAdd(typescriptModel.ClassType,
                                            new TypeScriptFile(typescriptModel.Model, typescriptModel.ClassType));
                                    }
                                }
                            }
                        }

                        sb.AppendLine($"[Controller /{controllerActionDescriptor.ControllerName} request type : {controllerActionDescriptor.ActionName}]");

                        // Handle parameters
                        foreach (var parameter in controllerActionDescriptor.Parameters)
                        {
                            sb.AppendLine($"\t\tParameter: {parameter.Name}, Type: {parameter.ParameterType}");

                            // Handle FromBody parameters
                            if (parameter.BindingInfo != null && parameter.BindingInfo.BindingSource == BindingSource.Body)
                            {
                                var parameterType = parameter.ParameterType;

                                TypeScriptGeneratedModels typeScriptGeneratedModels = null;
                                TypeScriptFile typeScriptGeneratedModel = null;
                                if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(List<>))
                                {
                                    var itemType = parameterType.GetGenericArguments()[0];
                                    sb.AppendLine($"\tParameter Type: List<{itemType.Name}>");

                                    if (CheckOutputTypeIsHandledByGeneratr(itemType, sb))
                                    {
                                        typeScriptGeneratedModels = typeScriptGenerator.Generate(itemType.Name, new TypeScriptGeneratedModels(), false);
                                        typeScriptGeneratedModel = new TypeScriptFile(
                                            typeScriptGeneratedModels.ToString(),
                                            itemType.Name);
                                    }
                                }
                                else
                                {
                                    sb.AppendLine($"\tParameter Type: {parameterType.Name}");
                                    if (CheckOutputTypeIsHandledByGeneratr(parameterType, sb))
                                    {
                                        typeScriptGeneratedModels = typeScriptGenerator.Generate(parameterType.Name, new TypeScriptGeneratedModels(), true);

                                        typeScriptGeneratedModel = new TypeScriptFile(
                                            typeScriptGeneratedModels.ToString(),
                                            parameterType.Name);
                                    }
                                }

                                if (typeScriptGeneratedModel != null)
                                {
                                    generatedTypescriptModels.TryAdd(typeScriptGeneratedModel.TypeName,
                                        typeScriptGeneratedModel);
                                    sb.AppendLine("***\r\n" + typeScriptGeneratedModel + "***\r\n");
                                    foreach (var typescriptModel in typeScriptGeneratedModels.GeneratedOjects)
                                    {
                                        if (generatedTypescriptModels.ContainsKey(typescriptModel.ClassType) == false)
                                        {
                                            generatedTypescriptModels.TryAdd(typescriptModel.ClassType,
                                                new TypeScriptFile(typescriptModel.Model, typescriptModel.ClassType));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (var generatedTypescriptModel in generatedTypescriptModels)
                {
                    AddEntryToZipArchive(zipArchive, generatedTypescriptModel.Value.FileName, generatedTypescriptModel.Value.FileContent);
                }
            }

            zipBytes = zipMemoryStream.ToArray();
        }

        return new AllTypescriptModels(sb.ToString(), zipBytes);
    }


    private List<string> _exludedTypesFromGenerationInTypeScript = new List<string>()
    {
        "FileContentResult"
    };
    private bool CheckOutputTypeIsHandledByGeneratr(Type producesAttributeType, StringBuilder sb)
    {
        if (_exludedTypesFromGenerationInTypeScript.Contains(producesAttributeType.Name))
        {
            sb.AppendLine($"Not handled type in typescript generator {producesAttributeType.Name}");
            return false;
        }

        return true;
    }

    private void AddEntryToZipArchive(ZipArchive zipArchive, string fileName, string fileContent)
    {
        ZipArchiveEntry zipEntry = zipArchive.CreateEntry(fileName);
        using (StreamWriter writer = new StreamWriter(zipEntry.Open()))
        {
            writer.Write(fileContent);
        }
    }
}