using System;
using System.Collections;
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
                            var methodInfo = controllerActionDescriptor.MethodInfo;
                            var httpMethod = GetHttpMethod(controllerActionDescriptor);

                            var parameters = new List<ServiceParameter>();
                            foreach (var parameter in controllerActionDescriptor.Parameters)
                            {
                                var parameterType = parameter.ParameterType;
                                var bindingSource = parameter.BindingInfo?.BindingSource;
                                var fromBody = bindingSource == BindingSource.Body;
                                var fromQuery = bindingSource == BindingSource.Query;
                                var fromRoute = bindingSource == BindingSource.Path || bindingSource == BindingSource.ModelBinding;
                                var controllerParameterDescriptor = parameter as ControllerParameterDescriptor;
                                var parameterInfo = controllerParameterDescriptor?.ParameterInfo;

                                parameters.Add(new ServiceParameter
                                {
                                    Name = parameter.Name,
                                    ParameterType = parameter.ParameterType,
                                    FromBody = fromBody,
                                    FromQuery = fromQuery,
                                    FromRoute = fromRoute,
                                    ParameterInfo = parameterInfo
                                });

                                GenerateModelsForType(parameterType, typeScriptGenerator, generatedTypescriptModels, generatedModelTypes);
                            }

                            var returnType = GetActionReturnType(methodInfo);
                            GenerateModelsForType(returnType, typeScriptGenerator, generatedTypescriptModels, generatedModelTypes);

                            serviceMethods.Add(new ServiceMethod
                            {
                                ControllerName = controllerName,
                                ActionName = controllerActionDescriptor.ActionName,
                                HttpMethod = httpMethod,
                                Parameters = parameters,
                                ReturnType = returnType
                            });
                        }
                    }

                    foreach (var generatedTypescriptModel in generatedTypescriptModels.Values)
                    {
                        AddEntryToZipArchive(zipArchive, generatedTypescriptModel.FileName, generatedTypescriptModel.FileContent);
                    }

                    var serviceGenerator = new TypeScriptServiceGenerator();
                    generatedServices = serviceGenerator.GenerateServices(serviceMethods);

                    foreach (var service in generatedServices)
                    {
                        var serviceFileName = $"api-services/{ToKebabCase(service.Key)}.service.ts";
                        AddEntryToZipArchive(zipArchive, serviceFileName, service.Value);
                    }

                    var apiUrlTokenFileContent =
@"import { InjectionToken } from '@angular/core';

export const API_URL = new InjectionToken<string>('API_URL');
";
                    AddEntryToZipArchive(zipArchive, "models/api-url.token.ts", apiUrlTokenFileContent);
                }

                zipBytes = zipMemoryStream.ToArray();
            }

            return new AllTypescriptModels(
                sb.ToString(),
                zipBytes,
                generatedModelTypes.Select(x => x.Name).ToList(),
                generatedServices
            );
        }

        private string GetHttpMethod(ControllerActionDescriptor actionDescriptor)
        {
            var method = "GET";
            var httpMethodAttributes = actionDescriptor.MethodInfo
                .GetCustomAttributes()
                .OfType<HttpMethodAttribute>();

            if (httpMethodAttributes.Any())
            {
                method = httpMethodAttributes.First().HttpMethods.First();
            }
            return method;
        }

        private Type GetActionReturnType(MethodInfo methodInfo)
        {
            var returnType = methodInfo.ReturnType;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                returnType = returnType.GetGenericArguments()[0];
            }
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ActionResult<>))
            {
                returnType = returnType.GetGenericArguments()[0];
            }
            var producesAttribute = methodInfo
                .GetCustomAttributes()
                .FirstOrDefault(x => x.GetType().Name.StartsWith("Produces")) as dynamic;

            if (producesAttribute != null && producesAttribute.Type != null)
            {
                returnType = producesAttribute.Type;
            }
            return returnType;
        }

        private void GenerateModelsForType(
            Type type,
            TypeScriptGenerator typeScriptGenerator,
            Dictionary<string, TypeScriptFile> generatedTypescriptModels,
            HashSet<Type> generatedModelTypes)
        {
            if (type == null || IsBuiltInType(type) || generatedModelTypes.Contains(type))
            {
                return;
            }
            generatedModelTypes.Add(type);

            var typeScriptGeneratedModels = typeScriptGenerator.Generate(type, new TypeScriptGeneratedModels(), false);
            AddGeneratedModels(typeScriptGeneratedModels, generatedTypescriptModels, generatedModelTypes);

            if (type.IsGenericType)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    GenerateModelsForType(arg, typeScriptGenerator, generatedTypescriptModels, generatedModelTypes);
                }
            }
            else if (IsCollectionType(type))
            {
                var itemType = GetCollectionItemType(type);
                GenerateModelsForType(itemType, typeScriptGenerator, generatedTypescriptModels, generatedModelTypes);
            }
        }

        private bool IsBuiltInType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive
                || t == typeof(string)
                || t == typeof(decimal)
                || t == typeof(DateTime)
                || t == typeof(Guid);
        }

        private bool IsCollectionType(Type type)
        {
            return typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string);
        }

        private Type GetCollectionItemType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }
            if (type.IsGenericType)
            {
                return type.GetGenericArguments().First();
            }
            return typeof(object);
        }

        private void AddGeneratedModels(
    TypeScriptGeneratedModels typeScriptGeneratedModels,
    Dictionary<string, TypeScriptFile> generatedTypescriptModels,
    HashSet<Type> generatedModelTypes)
{
    foreach (var tsModel in typeScriptGeneratedModels.GeneratedModels)
    {
        var originalType = tsModel.OriginalType;
        var fixedModel = tsModel.Model;

        // Usuwamy wszystko "?: string" -> ": string" (fallback)
        fixedModel = fixedModel.Replace("?: string", ": string");

        if (originalType != null)
        {
            var props = originalType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                var tsPropName = ToCamelCase(p.Name) + ": string";

                // Sprawdzamy nazwy atrybutów
                var hasNullableStringProperty = p.GetCustomAttributes()
                    .Any(a => a.GetType().Name == "NullableStringPropertyAttribute");

                var hasNullableGuidProperty = p.GetCustomAttributes()
                    .Any(a => a.GetType().Name == "NullableGuidPropertyAttribute");

                // Jeśli to string? i ma [NullableStringProperty]
                if (hasNullableStringProperty && p.PropertyType == typeof(string))
                {
                    var find = tsPropName;
                    var repl = ToCamelCase(p.Name) + "?: string"; 
                    fixedModel = fixedModel.Replace(find, repl);
                }

                // Jeśli to Guid? i ma [NullableGuidProperty]
                if (hasNullableGuidProperty &&
                    (p.PropertyType == typeof(Guid?) || p.PropertyType == typeof(Nullable<Guid>)))
                {
                    var find = tsPropName;
                    var repl = ToCamelCase(p.Name) + "?: string"; 
                    // lub "?: string | null" jeśli wolisz
                    fixedModel = fixedModel.Replace(find, repl);
                }
            }
        }

        var file = new TypeScriptFile(fixedModel, tsModel.TypeName);
        if (!generatedTypescriptModels.ContainsKey(file.TypeName))
        {
            generatedTypescriptModels.Add(file.TypeName, file);
            if (tsModel.OriginalType != null)
            {
                generatedModelTypes.Add(tsModel.OriginalType);
            }
        }
    }
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

        private string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            var sb = new StringBuilder();
            sb.Append(char.ToLowerInvariant(input[0]));
            for (int i = 1; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsUpper(c))
                {
                    sb.Append('-');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private string ToCamelCase(string csharpPropertyName)
        {
            if (string.IsNullOrEmpty(csharpPropertyName))
                return csharpPropertyName;
            if (csharpPropertyName.Length == 1)
                return csharpPropertyName.ToLowerInvariant();
            return char.ToLowerInvariant(csharpPropertyName[0]) + csharpPropertyName.Substring(1);
        }
    }
}
