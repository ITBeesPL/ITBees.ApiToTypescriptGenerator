using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

namespace ITBees.ApiToTypescriptGenerator.Services
{
    public class TypescriptGeneratorService : ITypescriptGeneratorService
    {
        private readonly IServiceProvider _serviceProvider;

        public TypescriptGeneratorService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Returns all registered controllers with their input parameters, methods, and returning types as TypeScript interfaces.
        /// </summary>
        public AllTypescriptModels GetAllControllersWithTypescriptModels()
        {
            var partManager = _serviceProvider.GetRequiredService<ApplicationPartManager>();
            var feature = new ControllerFeature();
            partManager.PopulateFeature(feature);
            var actionDescriptorCollectionProvider = _serviceProvider.GetRequiredService<IActionDescriptorCollectionProvider>();
            var sb = new StringBuilder();
            var typeScriptGenerator = new TypeScriptGenerator();
            var generatedTypescriptModels = new Dictionary<string, TypeScriptFile>();
            var generatedModelTypes = new HashSet<Type>(); // Collect the generated model types
            byte[] zipBytes;
            Dictionary<string, string> generatedServices = null;

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

                                    if (CheckOutputTypeIsHandledByGenerator(genericTypeDefinition, sb))
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
                                                generatedModelTypes.Add(typescriptModel.OriginalType);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    sb.AppendLine($"\tProduces Type: {producedType.Name}");
                                    if (CheckOutputTypeIsHandledByGenerator(producedType, sb))
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
                                                generatedModelTypes.Add(typescriptModel.OriginalType);
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

                                        if (CheckOutputTypeIsHandledByGenerator(genericTypeDefinition, sb))
                                        {
                                            // Generate the TypeScript model with generic type arguments
                                            typeScriptGeneratedModels = typeScriptGenerator.Generate(genericTypeDefinition.Name, new TypeScriptGeneratedModels(), false, genericTypeArguments);

                                            var interfaceName = GetInterfaceName(genericTypeDefinition, genericTypeArguments);
                                            // Retrieve the specific TypescriptModel
                                            var typescriptModel = typeScriptGeneratedModels.GeneratedModels.FirstOrDefault(m => m.TypeName == interfaceName);
                                            if (typescriptModel != null)
                                            {
                                                typeScriptGeneratedModel = new TypeScriptFile(
                                                    typescriptModel.Model,
                                                    interfaceName);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        sb.AppendLine($"\tParameter Type: {parameterType.Name}");
                                        if (CheckOutputTypeIsHandledByGenerator(parameterType, sb))
                                        {
                                            typeScriptGeneratedModels = typeScriptGenerator.Generate(parameterType.Name, new TypeScriptGeneratedModels(), true);

                                            var interfaceName = parameterType.Name;
                                            // Retrieve the specific TypescriptModel
                                            var typescriptModel = typeScriptGeneratedModels.GeneratedModels.FirstOrDefault(m => m.TypeName == interfaceName);
                                            if (typescriptModel != null)
                                            {
                                                typeScriptGeneratedModel = new TypeScriptFile(
                                                    typescriptModel.Model,
                                                    interfaceName);
                                            }
                                        }
                                    }

                                    if (typeScriptGeneratedModel != null)
                                    {
                                        generatedTypescriptModels.TryAdd(typeScriptGeneratedModel.TypeName, typeScriptGeneratedModel);
                                        generatedModelTypes.Add(parameterType);
                                        sb.AppendLine("***\r\n" + typeScriptGeneratedModel + "***\r\n");
                                        foreach (var tsModel in typeScriptGeneratedModels.GeneratedModels)
                                        {
                                            if (!generatedTypescriptModels.ContainsKey(tsModel.TypeName))
                                            {
                                                generatedTypescriptModels.Add(tsModel.TypeName, new TypeScriptFile(tsModel.Model, tsModel.TypeName));
                                                if (tsModel.OriginalType != null)
                                                {
                                                    generatedModelTypes.Add(tsModel.OriginalType);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Write models to zip archive
                    foreach (var generatedTypescriptModel in generatedTypescriptModels.Values)
                    {
                        AddEntryToZipArchive(zipArchive, generatedTypescriptModel.FileName, generatedTypescriptModel.FileContent);
                    }

                    // Now generate the services
                    var serviceGenerator = new TypeScriptServiceGenerator();
                    generatedServices = serviceGenerator.GenerateServices(generatedModelTypes.ToList());

                    // Write the generated services to the zip file
                    foreach (var service in generatedServices)
                    {
                        var serviceFileName = $"api-services/{ToKebabCase(service.Key)}.service.ts";
                        var zipEntry = zipArchive.CreateEntry(serviceFileName);
                        using (var entryStream = zipEntry.Open())
                        using (var streamWriter = new StreamWriter(entryStream))
                        {
                            streamWriter.Write(service.Value);
                        }
                    }
                }

                zipBytes = zipMemoryStream.ToArray();
            }

            return new AllTypescriptModels(sb.ToString(), zipBytes, generatedModelTypes.Select(t => t.Name).ToList(), generatedServices);
        }

        // Helper methods
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

        private string ToKebabCase(string input)
        {
            // Convert PascalCase to kebab-case
            return System.Text.RegularExpressions.Regex.Replace(input, "(\\B[A-Z])", "-$1").ToLower();
        }

        private void AddEntryToZipArchive(ZipArchive zipArchive, string fileName, string fileContent)
        {
            var zipEntry = zipArchive.CreateEntry(fileName);
            using (var entryStream = zipEntry.Open())
            using (var streamWriter = new StreamWriter(entryStream))
            {
                streamWriter.Write(fileContent);
            }
        }

        private List<string> _excludedTypesFromGenerationInTypeScript = new List<string>()
        {
            "FileContentResult"
        };

        private bool CheckOutputTypeIsHandledByGenerator(Type type, StringBuilder sb)
        {
            if (_excludedTypesFromGenerationInTypeScript.Contains(type.Name))
            {
                sb.AppendLine($"Not handled type in TypeScript generator: {type.Name}");
                return false;
            }

            return true;
        }
    }
}
