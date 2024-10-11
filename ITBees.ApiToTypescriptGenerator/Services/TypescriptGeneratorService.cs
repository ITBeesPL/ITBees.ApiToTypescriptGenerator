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
using Microsoft.AspNetCore.Mvc.Routing;
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
            var controllers = new List<ControllerInfo>();
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
                            var controllerName = controllerActionDescriptor.ControllerName;
                            var endpointName = GetEndpointName(controllerName);

                            // Check if this controller is already in our list
                            var controllerInfo = controllers.FirstOrDefault(c => c.ControllerName == controllerName);
                            if (controllerInfo == null)
                            {
                                controllerInfo = new ControllerInfo
                                {
                                    ControllerName = controllerName,
                                    EndpointName = endpointName,
                                    Actions = new List<ActionInfo>()
                                };
                                controllers.Add(controllerInfo);
                            }

                            // Get the HTTP method
                            string httpMethod = GetHttpMethod(controllerActionDescriptor.MethodInfo);

                            // Get the return type
                            Type returnType = null;
                            var producesAttribute = controllerActionDescriptor.MethodInfo.GetCustomAttribute<ProducesAttribute>();
                            if (producesAttribute != null && producesAttribute.Type != null)
                            {
                                returnType = producesAttribute.Type;
                            }

                            // Get the parameter type (assumes one parameter for simplicity)
                            Type parameterType = null;
                            if (controllerActionDescriptor.Parameters != null && controllerActionDescriptor.Parameters.Count > 0)
                            {
                                var parameter = controllerActionDescriptor.Parameters.FirstOrDefault(p => p.BindingInfo?.BindingSource == BindingSource.Body);
                                if (parameter != null)
                                {
                                    parameterType = parameter.ParameterType;
                                }
                            }

                            // Generate TypeScript models for return and parameter types
                            if (returnType != null && CheckOutputTypeIsHandledByGenerator(returnType, sb))
                            {
                                var typeScriptGeneratedModels = typeScriptGenerator.Generate(returnType.Name, new TypeScriptGeneratedModels(), true);
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

                            if (parameterType != null && CheckOutputTypeIsHandledByGenerator(parameterType, sb))
                            {
                                var typeScriptGeneratedModels = typeScriptGenerator.Generate(parameterType.Name, new TypeScriptGeneratedModels(), true);
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

                            // Add the action info
                            var actionInfo = new ActionInfo
                            {
                                ActionName = controllerActionDescriptor.ActionName,
                                HttpMethod = httpMethod,
                                ReturnType = returnType,
                                ParameterType = parameterType
                            };

                            controllerInfo.Actions.Add(actionInfo);
                        }
                    }

                    // Write models to zip archive
                    foreach (var generatedTypescriptModel in generatedTypescriptModels.Values)
                    {
                        AddEntryToZipArchive(zipArchive, generatedTypescriptModel.FileName, generatedTypescriptModel.FileContent);
                    }

                    // Now generate the services
                    var serviceGenerator = new TypeScriptServiceGenerator();
                    generatedServices = serviceGenerator.GenerateServices(controllers);

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

            return new AllTypescriptModels(sb.ToString(), zipBytes, generatedTypescriptModels.Keys.ToList(), generatedServices);
        }



        // Helper methods

        private string GetEndpointName(string controllerName)
        {
            if (controllerName.EndsWith("Controller"))
            {
                controllerName = controllerName.Substring(0, controllerName.Length - "Controller".Length);
            }
            return char.ToLowerInvariant(controllerName[0]) + controllerName.Substring(1);
        }

        private string GetHttpMethod(MethodInfo methodInfo)
        {
            var httpMethodAttrs = methodInfo.GetCustomAttributes(true)
                .OfType<HttpMethodAttribute>();

            if (httpMethodAttrs.Any())
            {
                return httpMethodAttrs.First().HttpMethods.First();
            }
            else
            {
                // Default to POST if no HTTP method attribute is found
                return "POST";
            }
        }

        private bool CheckOutputTypeIsHandledByGenerator(Type type, StringBuilder sb)
        {
            if (_excludedTypesFromGenerationInTypeScript.Contains(type.Name))
            {
                sb.AppendLine($"Not handled type in TypeScript generator: {type.Name}");
                return false;
            }

            return true;
        }


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
    }
}
