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
        /// Generates TypeScript models and services based on the registered controllers and actions.
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
            var generatedModelTypes = new HashSet<Type>();
            var serviceMethods = new List<ServiceMethod>();

            byte[] zipBytes;
            Dictionary<string, string> generatedServices = null;

            using (var zipMemoryStream = new MemoryStream())
            {
                using (var zipArchive = new ZipArchive(zipMemoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var actionDescriptor in actionDescriptorCollectionProvider.ActionDescriptors.Items)
                    {
                        if (actionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                        {
                            var controllerName = controllerActionDescriptor.ControllerName;
                            var actionName = controllerActionDescriptor.ActionName;
                            var methodInfo = controllerActionDescriptor.MethodInfo;

                            sb.AppendLine($"[Controller /{controllerName}]");
                            sb.AppendLine($"Action: {actionName}");

                            // Determine HTTP method
                            var httpMethod = GetHttpMethod(controllerActionDescriptor);

                            // Collect parameters
                            var parameters = new List<ServiceParameter>();
                            foreach (var parameter in controllerActionDescriptor.Parameters)
                            {
                                var parameterType = parameter.ParameterType;
                                var bindingSource = parameter.BindingInfo?.BindingSource;
                                var fromBody = bindingSource == BindingSource.Body;
                                var fromQuery = bindingSource == BindingSource.Query;
                                var fromRoute = bindingSource == BindingSource.Path || bindingSource == BindingSource.ModelBinding;

                                parameters.Add(new ServiceParameter
                                {
                                    Name = parameter.Name,
                                    ParameterType = parameterType,
                                    FromBody = fromBody,
                                    FromQuery = fromQuery,
                                    FromRoute = fromRoute
                                });

                                // Generate TypeScript models for parameter types
                                if (CheckTypeNeedsGeneration(parameterType))
                                {
                                    var typeScriptGeneratedModels = typeScriptGenerator.Generate(parameterType.Name, new TypeScriptGeneratedModels(), true);
                                    AddGeneratedModels(typeScriptGeneratedModels, generatedTypescriptModels, generatedModelTypes);
                                }
                            }

                            // Get return type and generate models
                            var returnType = GetActionReturnType(methodInfo);
                            if (CheckTypeNeedsGeneration(returnType))
                            {
                                var typeScriptGeneratedModels = typeScriptGenerator.Generate(returnType.Name, new TypeScriptGeneratedModels(), true);
                                AddGeneratedModels(typeScriptGeneratedModels, generatedTypescriptModels, generatedModelTypes);
                            }

                            // Add service method
                            var serviceMethod = new ServiceMethod
                            {
                                ControllerName = controllerName,
                                ActionName = actionName,
                                HttpMethod = httpMethod,
                                Parameters = parameters,
                                ReturnType = returnType
                            };
                            serviceMethods.Add(serviceMethod);
                        }
                    }

                    // Write models to zip archive
                    foreach (var generatedTypescriptModel in generatedTypescriptModels.Values)
                    {
                        AddEntryToZipArchive(zipArchive, generatedTypescriptModel.FileName, generatedTypescriptModel.FileContent);
                    }

                    // Generate the services
                    var serviceGenerator = new TypeScriptServiceGenerator();
                    generatedServices = serviceGenerator.GenerateServices(serviceMethods);

                    // Write the generated services to the zip file
                    foreach (var service in generatedServices)
                    {
                        var serviceFileName = $"api-services/{ToKebabCase(service.Key)}.service.ts";
                        AddEntryToZipArchive(zipArchive, serviceFileName, service.Value);
                    }
                }

                zipBytes = zipMemoryStream.ToArray();
            }

            return new AllTypescriptModels(sb.ToString(), zipBytes, generatedModelTypes.Select(t => t.Name).ToList(), generatedServices);
        }

        // Helper method to get HTTP method
        private string GetHttpMethod(ControllerActionDescriptor actionDescriptor)
        {
            var method = "GET"; // Default method

            var httpMethodAttributes = actionDescriptor.MethodInfo.GetCustomAttributes()
                .OfType<HttpMethodAttribute>();

            if (httpMethodAttributes.Any())
            {
                method = httpMethodAttributes.First().HttpMethods.First();
            }

            return method;
        }

        // Helper method to get the actual return type
        private Type GetActionReturnType(MethodInfo methodInfo)
        {
            var returnType = methodInfo.ReturnType;

            // Unwrap Task<> if it's an async method
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                returnType = returnType.GetGenericArguments()[0];
            }

            // Handle ActionResult<T>
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ActionResult<>))
            {
                returnType = returnType.GetGenericArguments()[0];
            }

            return returnType;
        }

        // Helper method to check if a type needs TypeScript model generation
        private bool CheckTypeNeedsGeneration(Type type)
        {
            if (type == null || type == typeof(void))
                return false;

            if (IsBuiltInType(type))
                return false;

            return true;
        }

        // Helper method to determine if a type is a built-in type
        private bool IsBuiltInType(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.IsPrimitive
                || underlyingType == typeof(string)
                || underlyingType == typeof(decimal)
                || underlyingType == typeof(DateTime)
                || underlyingType == typeof(Guid);
        }

        // Helper method to add generated models to the collections
        private void AddGeneratedModels(TypeScriptGeneratedModels typeScriptGeneratedModels, Dictionary<string, TypeScriptFile> generatedTypescriptModels, HashSet<Type> generatedModelTypes)
        {
            foreach (var typescriptModel in typeScriptGeneratedModels.GeneratedModels)
            {
                var typeScriptGeneratedModel = new TypeScriptFile(
                    typescriptModel.Model,
                    typescriptModel.TypeName);

                if (!generatedTypescriptModels.ContainsKey(typeScriptGeneratedModel.TypeName))
                {
                    generatedTypescriptModels.Add(typeScriptGeneratedModel.TypeName, typeScriptGeneratedModel);
                    if (typescriptModel.OriginalType != null)
                    {
                        generatedModelTypes.Add(typescriptModel.OriginalType);
                    }
                }
            }
        }

        // Helper method to add an entry to the ZIP archive
        private void AddEntryToZipArchive(ZipArchive zipArchive, string fileName, string fileContent)
        {
            var zipEntry = zipArchive.CreateEntry(fileName);
            using (var entryStream = zipEntry.Open())
            using (var streamWriter = new StreamWriter(entryStream))
            {
                streamWriter.Write(fileContent);
            }
        }

        // Helper method to convert a string to kebab-case
        private string ToKebabCase(string input)
        {
            return System.Text.RegularExpressions.Regex.Replace(input, "(\\B[A-Z])", "-$1").ToLower();
        }
    }
}
