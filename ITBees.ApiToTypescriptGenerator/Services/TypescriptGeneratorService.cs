using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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

            var actionDescriptorCollectionProvider =
                _serviceProvider.GetRequiredService<IActionDescriptorCollectionProvider>();

            var sb = new StringBuilder();
            var typeScriptGenerator = new TypeScriptGenerator();
            var generatedTypescriptModels = new Dictionary<string, TypeScriptFile>();
            var generatedModelTypes = new HashSet<Type>();
            var serviceMethods = new List<ServiceMethod>();

            Dictionary<string, string> generatedServices = null;
            byte[] zipBytes;
            var addedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                                var fromRoute = bindingSource == BindingSource.Path ||
                                                bindingSource == BindingSource.ModelBinding;
                                var controllerParameterDescriptor = parameter as ControllerParameterDescriptor;
                                var parameterInfo = controllerParameterDescriptor?.ParameterInfo;

                                parameters.Add(new ServiceParameter
                                {
                                    Name = parameter.Name,
                                    ParameterType = parameterType,
                                    FromBody = fromBody,
                                    FromQuery = fromQuery,
                                    FromRoute = fromRoute,
                                    ParameterInfo = parameterInfo
                                });

                                GenerateModelsForType(parameterType, typeScriptGenerator, generatedTypescriptModels,
                                    generatedModelTypes);
                            }

                            var returnType = GetActionReturnType(methodInfo);
                            GenerateModelsForType(returnType, typeScriptGenerator, generatedTypescriptModels,
                                generatedModelTypes);

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

                    var serviceGenerator = new TypeScriptServiceGenerator();
                    generatedServices = serviceGenerator.GenerateServices(serviceMethods);

                    foreach (var service in generatedServices)
                    {
                        var serviceFileName = $"api-services/{ToKebabCase(service.Key)}.service.ts";
                        AddEntryToZipArchive(zipArchive, serviceFileName, service.Value, addedFileNames);
                    }

                    var apiUrlTokenFileContent =
                        @"import { InjectionToken } from '@angular/core';

export const API_URL = new InjectionToken<string>('API_URL');
";
                    AddEntryToZipArchive(zipArchive, "models/api-url.token.ts", apiUrlTokenFileContent, addedFileNames);

                    GenerateScreenViewInterfaces(zipArchive, addedFileNames);
                    GenerateNotificationMethodServices(zipArchive, typeScriptGenerator, generatedTypescriptModels,
                        generatedModelTypes, addedFileNames);
                    GenerateAdditionalModels(typeScriptGenerator, generatedTypescriptModels, generatedModelTypes);
                    GenerateExpectedModelTypes(typeScriptGenerator, generatedTypescriptModels, generatedModelTypes);

                    foreach (var generatedTypescriptModel in generatedTypescriptModels.Values)
                    {
                        AddEntryToZipArchive(zipArchive, generatedTypescriptModel.FileName,
                            generatedTypescriptModel.FileContent, addedFileNames);
                    }
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

        private void GenerateNotificationMethodServices(
            ZipArchive zipArchive,
            TypeScriptGenerator typeScriptGenerator,
            Dictionary<string, TypeScriptFile> generatedTypescriptModels,
            HashSet<Type> generatedModelTypes,
            HashSet<string> addedFileNames)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var notificationMethodTypes = assemblies
                    .SelectMany(a =>
                    {
                        try
                        {
                            return a.GetTypes();
                        }
                        catch
                        {
                            return new Type[0];
                        }
                    })
                    .Where(t => typeof(IHubMethod).IsAssignableFrom(t)
                                && t.IsClass && !t.IsAbstract)
                    .ToList();

                foreach (var methodType in notificationMethodTypes)
                {
                    var nameProperty = methodType.GetProperty("Name");
                    var instance = Activator.CreateInstance(methodType);
                    var rawMethodName = nameProperty != null
                        ? nameProperty.GetValue(instance)?.ToString()
                        : methodType.Name;

                    if (string.IsNullOrEmpty(rawMethodName))
                        rawMethodName = methodType.Name;

                    var executeMethod = methodType.GetMethod("ExecuteAsync");
                    // Sprawdzamy zarówno ExpectedModelTypeAttribute jak i ExpectedOutputModelTypeAttribute
                    var expectedModelAttr = executeMethod?.GetCustomAttributes(true)
                        .FirstOrDefault(x => x.GetType().Name == "ExpectedModelTypeAttribute" ||
                                             x.GetType().Name == "ExpectedOutputModelTypeAttribute");

                    var tsParameterType = "any";
                    string importModelName = null;
                    if (expectedModelAttr != null)
                    {
                        var attrTypeProp = expectedModelAttr.GetType().GetProperty("Type");
                        var csharpModelType = attrTypeProp?.GetValue(expectedModelAttr) as Type;
                        if (csharpModelType != null)
                        {
                            GenerateModelsForType(csharpModelType, typeScriptGenerator, generatedTypescriptModels,
                                generatedModelTypes);
                            var csharpModelName = csharpModelType.Name;
                            tsParameterType = "I" + csharpModelName;
                            importModelName = tsParameterType;
                        }
                    }

                    var isCommand = typeof(IHubCommand).IsAssignableFrom(methodType);
                    var isListener = typeof(IHubListener).IsAssignableFrom(methodType);

                    // Wyznaczamy suffix i wstępnie format nazwy pliku
                    var suffix = isListener ? "listener-base" : (isCommand ? "command-base" : "");
                    var methodName = rawMethodName;

                    // Usunięcie "Command"/"Listener" z nazwy metody, by nie dublować w pliku
                    if (isCommand && methodName.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
                    {
                        methodName = methodName[..^"Command".Length];
                    }

                    if (isListener && methodName.EndsWith("Listener", StringComparison.OrdinalIgnoreCase))
                    {
                        methodName = methodName[..^"Listener".Length];
                    }

                    // Nazwa pliku
                    var fileName = $"websocket-services/{ToKebabCase(methodName)}-{suffix}.ts";

                    // Budujemy nazwę klasy bazowej
                    // 1) Usuwamy "Method"
                    var baseClassName = methodType.Name.Replace("Method", "", StringComparison.OrdinalIgnoreCase);
                    // 2) Usuwamy "Command"/"Listener" (o ile jest)
                    if (isCommand && baseClassName.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
                    {
                        baseClassName = baseClassName[..^"Command".Length];
                    }

                    if (isListener && baseClassName.EndsWith("Listener", StringComparison.OrdinalIgnoreCase))
                    {
                        baseClassName = baseClassName[..^"Listener".Length];
                    }

                    // 3) Doklejamy "CommandBase"/"ListenerBase" zależnie od suffix
                    string className;
                    if (isCommand)
                        className = $"{baseClassName}CommandBase";
                    else if (isListener)
                        className = $"{baseClassName}ListenerBase";
                    else
                        className = baseClassName;

                    // Poprawa na styl PascalCase
                    className = ToPascalCase(className);

                    var importPart = "";
                    if (importModelName != null)
                    {
                        var modelNameForFile = importModelName;
                        if (modelNameForFile.StartsWith("I") && modelNameForFile.Length > 1 &&
                            char.IsUpper(modelNameForFile[1]))
                        {
                            modelNameForFile = modelNameForFile.Substring(1);
                        }

                        var modelFileName = ToKebabCase(modelNameForFile);
                        importPart = $"import {{ {importModelName} }} from '../{modelFileName}.model';\n";
                    }

                    string fileContent;

                    if (isCommand)
                    {
                        // Komenda – brak mechanizmu "on(...)"
                        // Wstawiamy np. metodę "send" do wysyłania danych do huba
                        fileContent = $@"{importPart}import {{ HubConnection }} from '@microsoft/signalr';

export abstract class {className} {{
    protected methodName = '{rawMethodName}';
    protected hubConnection: HubConnection;

    protected constructor(hubConnection: HubConnection) {{
        this.hubConnection = hubConnection;
    }}

    getName() {{
        return this.methodName;
    }}

    public send(payload: {tsParameterType}) {{
        return this.hubConnection.invoke(this.methodName, payload);
    }}
}}";
                    }
                    else if (isListener)
                    {
                        // Listener – logika rejestracji onMessage z typem parametru z atrybutu
                        fileContent = $@"{importPart}import {{ HubConnection }} from '@microsoft/signalr';

export abstract class {className} {{
    protected methodName = '{rawMethodName}';
    protected hubConnection: HubConnection;

    protected constructor(hubConnection: HubConnection) {{
        this.hubConnection = hubConnection;
        this.hubConnection.on(this.methodName, (data: {tsParameterType}) => {{
            this.onMessage(data);
        }});
    }}

    getName() {{
        return this.methodName;
    }}

    protected abstract onMessage(data: {tsParameterType}): void;
}}";
                    }
                    else
                    {
                        fileContent = $@"import {{ HubConnection }} from '@microsoft/signalr';

export abstract class {className} {{
    protected methodName = '{rawMethodName}';
    protected hubConnection: HubConnection;

    protected constructor(hubConnection: HubConnection) {{
        this.hubConnection = hubConnection;
    }}

    getName() {{
        return this.methodName;
    }}
}}";
                    }

                    AddEntryToZipArchive(zipArchive, fileName, fileContent, addedFileNames);
                }
            }
            catch
            {
            }
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new StringBuilder();
            bool capitalize = true;
            foreach (var c in input)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(capitalize ? char.ToUpperInvariant(c) : c);
                    capitalize = false;
                }
                else
                {
                    capitalize = true;
                }
            }

            return sb.ToString();
        }

        private void GenerateAdditionalModels(
            TypeScriptGenerator typeScriptGenerator,
            Dictionary<string, TypeScriptFile> generatedTypescriptModels,
            HashSet<Type> generatedModelTypes)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes().Where(t =>
                                 t.IsClass &&
                                 (t.Name.EndsWith("Im") ||
                                  t.Name.EndsWith("Vm") ||
                                  t.Name.EndsWith("Um") ||
                                  t.Name.EndsWith("Dm")) &&
                                 t.GetCustomAttributes().Any(attr =>
                                     attr.GetType().Name == "InputModelTypeAttribute" ||
                                     attr.GetType().Name == "ExpectedModelTypeAttribute" ||
                                     attr.GetType().Name == "ExpectedOutputModelTypeAttribute") &&
                                 !IsBuiltInType(t)))
                    {
                        Debug.WriteLine($"GenerateAdditionalModels : {type.Name}");
                        GenerateModelsForType(type, typeScriptGenerator, generatedTypescriptModels,
                            generatedModelTypes);
                    }
                }
                catch
                {
                }
            }
        }

        private void GenerateExpectedModelTypes(
            TypeScriptGenerator typeScriptGenerator,
            Dictionary<string, TypeScriptFile> generatedTypescriptModels,
            HashSet<Type> generatedModelTypes)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var methods =
                            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                        foreach (var method in methods)
                        {
                            var expectedAttr = method.GetCustomAttributes(true)
                                .FirstOrDefault(x => x.GetType().Name == "ExpectedModelTypeAttribute" ||
                                                     x.GetType().Name == "ExpectedOutputModelTypeAttribute");
                            if (expectedAttr != null)
                            {
                                var attrTypeProp = expectedAttr.GetType().GetProperty("Type");
                                var csharpModelType = attrTypeProp?.GetValue(expectedAttr) as Type;
                                if (csharpModelType != null)
                                {
                                    GenerateModelsForType(csharpModelType, typeScriptGenerator,
                                        generatedTypescriptModels, generatedModelTypes);
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private void GenerateScreenViewInterfaces(ZipArchive zipArchive, HashSet<string> addedFileNames)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var screenViewTypes = assemblies
                    .SelectMany(a =>
                    {
                        try
                        {
                            return a.GetTypes();
                        }
                        catch
                        {
                            return new Type[0];
                        }
                    })
                    .Where(t => typeof(IScreenView).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                    .ToList();

                foreach (var screenViewType in screenViewTypes)
                {
                    var interfaceName = $"I{screenViewType.Name}";
                    var viewActionsProp =
                        screenViewType.GetProperty("ViewActions", BindingFlags.Public | BindingFlags.Instance);

                    if (viewActionsProp == null || viewActionsProp.PropertyType != typeof(List<ViewAction>))
                        continue;

                    object instance = null;
                    try
                    {
                        instance = Activator.CreateInstance(screenViewType);
                    }
                    catch
                    {
                        continue;
                    }

                    var viewActions = viewActionsProp.GetValue(instance) as List<ViewAction>;
                    if (viewActions == null) continue;

                    var sb = new StringBuilder();
                    sb.AppendLine($"export interface {interfaceName} {{");
                    foreach (var action in viewActions)
                    {
                        var actionMethodName = action.GetType().Name;
                        sb.AppendLine($"    {actionMethodName}();");
                    }

                    sb.AppendLine("}");

                    var fileName = $"views/{ToKebabCase(screenViewType.Name)}.view.ts";
                    AddEntryToZipArchive(zipArchive, fileName, sb.ToString(), addedFileNames);
                }
            }
            catch
            {
            }
        }

        private string GetHttpMethod(ControllerActionDescriptor actionDescriptor)
        {
            var method = "GET";
            var httpMethodAttributes = actionDescriptor.MethodInfo.GetCustomAttributes().OfType<HttpMethodAttribute>();
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

            var producesAttribute =
                methodInfo.GetCustomAttributes()
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
            if (type == null || IsBuiltInType(type) || generatedModelTypes.Contains(type)) return;

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
                var fixedModel = tsModel.Model;
                fixedModel = fixedModel.Replace("?: string", ": string");

                var modelFileName = ToKebabCase(tsModel.TypeName);
                if (tsModel.TypeName.StartsWith("I") && tsModel.TypeName.Length > 1 &&
                    char.IsUpper(tsModel.TypeName[1]))
                {
                    modelFileName = ToKebabCase(tsModel.TypeName.Substring(1));
                }

                modelFileName = $"{modelFileName}.model.ts";

                var file = new TypeScriptFile(fixedModel, tsModel.TypeName, modelFileName);
                if (!generatedTypescriptModels.ContainsKey(file.TypeName))
                {
                    generatedTypescriptModels.Add(file.TypeName, file);
                }
            }
        }

        private void AddEntryToZipArchive(
            ZipArchive zipArchive,
            string fileName,
            string fileContent,
            HashSet<string> addedFileNames)
        {
            if (addedFileNames.Contains(fileName)) return;

            var zipEntry = zipArchive.CreateEntry(fileName);
            using (var entryStream = zipEntry.Open())
            using (var streamWriter = new StreamWriter(entryStream))
            {
                streamWriter.Write(fileContent);
            }

            addedFileNames.Add(fileName);
        }

        private string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
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
    }
}
