﻿using System.IO.Compression;
using System.Reflection;
using System.Text;
using ITBees.ApiToTypescriptGenerator.Interfaces;
using ITBees.ApiToTypescriptGenerator.Services.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
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

                        var producesAttribute = controllerActionDescriptor.MethodInfo.GetCustomAttribute<ProducesAttribute>();
                        sb.AppendLine("\tProduces :");
                        if (producesAttribute != null && producesAttribute.Type != null)
                        {
                            sb.AppendLine($"\tProduces Type: {producesAttribute.Type.Name}");

                            var typeScriptGeneratedModel = new TypeScriptFile(typeScriptGenerator.Generate(producesAttribute.Type.Name, new TypeScriptGeneratedModels()).ToString(), producesAttribute.Type.Name);
                            generatedTypescriptModels.TryAdd(typeScriptGeneratedModel.TypeName, typeScriptGeneratedModel);
                            sb.AppendLine("***\r\n" + typeScriptGeneratedModel + "***\r\n");
                        }
                        sb.AppendLine($"[Controller /{controllerActionDescriptor.ControllerName} request type : {controllerActionDescriptor.ActionName}]");
                        foreach (var parameter in controllerActionDescriptor.Parameters)
                        {
                            sb.AppendLine($"\t\tParameter: {parameter.Name}, Type: {parameter.ParameterType}");
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

    private void AddEntryToZipArchive(ZipArchive zipArchive, string fileName, string fileContent)
    {
        ZipArchiveEntry zipEntry = zipArchive.CreateEntry(fileName);
        using (StreamWriter writer = new StreamWriter(zipEntry.Open()))
        {
            writer.Write(fileContent);
        }
    }
}