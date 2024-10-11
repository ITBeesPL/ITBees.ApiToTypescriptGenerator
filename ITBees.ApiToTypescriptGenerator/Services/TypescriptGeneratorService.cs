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

                            if (producedType.IsGenericType)
                            {
                                var genericTypeDefinition = producedType.GetGenericTypeDefinition();
                                var genericTypeArguments = producedType.GetGenericArguments();
                                sb.AppendLine($"\tProduces Type: {genericTypeDefinition.Name}<{string.Join(", ", genericTypeArguments.Select(t => t.Name))}>");

                                if (CheckOutputTypeIsHandledByGeneratr(genericTypeDefinition, sb))
                                {
                                    var typeScriptGeneratedModels = typeScriptGenerator.Generate(genericTypeDefinition.Name, new TypeScriptGeneratedModels(), false, genericTypeArguments);

                                    // Process each generated TypescriptModel
                                    foreach (var typescriptModel in typeScriptGeneratedModels.GeneratedModels)
                                    {
                                        var typeScriptGeneratedModel = new TypeScriptFile(
                                            typescriptModel.Model,
                                            typescriptModel.TypeName);

                                        if (!generatedTypescriptModels.ContainsKey(typeScriptGeneratedModel.TypeName))
                                        {
                                            generatedTypescriptModels.Add(typeScriptGeneratedModel.TypeName, typeScriptGeneratedModel);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                sb.AppendLine($"\tProduces Type: {producedType.Name}");
                                if (CheckOutputTypeIsHandledByGeneratr(producedType, sb))
                                {
                                    var typeScriptGeneratedModels = typeScriptGenerator.Generate(producedType.Name, new TypeScriptGeneratedModels(), true);

                                    foreach (var typescriptModel in typeScriptGeneratedModels.GeneratedModels)
                                    {
                                        var typeScriptGeneratedModel = new TypeScriptFile(
                                            typescriptModel.Model,
                                            typescriptModel.TypeName);

                                        if (!generatedTypescriptModels.ContainsKey(typeScriptGeneratedModel.TypeName))
                                        {
                                            generatedTypescriptModels.Add(typeScriptGeneratedModel.TypeName, typeScriptGeneratedModel);
                                        }
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

                                if (parameterType.IsGenericType)
                                {
                                    var genericTypeDefinition = parameterType.GetGenericTypeDefinition();
                                    var genericTypeArguments = parameterType.GetGenericArguments();
                                    sb.AppendLine($"\tParameter Type: {genericTypeDefinition.Name}<{string.Join(", ", genericTypeArguments.Select(t => t.Name))}>");

                                    if (CheckOutputTypeIsHandledByGeneratr(genericTypeDefinition, sb))
                                    {
                                        // Generate the TypeScript model with generic type arguments
                                        typeScriptGeneratedModels = typeScriptGenerator.Generate(genericTypeDefinition.Name, new TypeScriptGeneratedModels(), false, genericTypeArguments);

                                        var interfaceName = GetInterfaceName(genericTypeDefinition, genericTypeArguments);
                                        typeScriptGeneratedModel = new TypeScriptFile(
                                            typeScriptGeneratedModels.ToString(),
                                            interfaceName);
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
                                    generatedTypescriptModels.TryAdd(typeScriptGeneratedModel.TypeName, typeScriptGeneratedModel);
                                    sb.AppendLine("***\r\n" + typeScriptGeneratedModel + "***\r\n");
                                    foreach (var typescriptModel in typeScriptGeneratedModels.GeneratedModels)
                                    {
                                        if (!generatedTypescriptModels.ContainsKey(typescriptModel.ClassType))
                                        {
                                            generatedTypescriptModels.TryAdd(typescriptModel.ClassType, new TypeScriptFile(typescriptModel.Model, typescriptModel.ClassType));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (var generatedTypescriptModel in generatedTypescriptModels.Values)
                {
                    AddEntryToZipArchive(zipArchive, generatedTypescriptModel.FileName, generatedTypescriptModel.FileContent);
                }
            }

            zipBytes = zipMemoryStream.ToArray();
        }

        return new AllTypescriptModels(sb.ToString(), zipBytes);
    }

    // Helper method to generate interface name based on generic type arguments
    private string GetInterfaceName(Type genericTypeDefinition, Type[] genericTypeArguments)
    {
        var baseName = RemoveViewModelDecorator(genericTypeDefinition.Name);
        if (baseName.Contains('`'))
        {
            baseName = baseName.Substring(0, baseName.IndexOf('`'));
        }
        var typeArgumentNames = genericTypeArguments.Select(t => RemoveViewModelDecorator(t.Name));
        var interfaceName = baseName + string.Join("", typeArgumentNames);
        return interfaceName;
    }

    // Adjust the RemoveViewModelDecorator method if needed
    private string RemoveViewModelDecorator(string viewModelName)
    {
        // Remove generic arity if present
        if (viewModelName.Contains('`'))
        {
            viewModelName = viewModelName.Substring(0, viewModelName.IndexOf('`'));
        }

        return viewModelName
            .Replace("ViewModel", "")
            .Replace("UpdateModel", "")
            .Replace("InputModel", "")
            .Replace("Dto", "");
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