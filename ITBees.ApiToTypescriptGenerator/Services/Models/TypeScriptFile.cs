using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace ITBees.ApiToTypescriptGenerator.Services.Models;

public class TypeScriptFile
{
    public string FileContent { get; }
    public string TypeName { get; }
    public string FileName { get; }

    public TypeScriptFile(string fileContent, string typeName, string fileName = "")
    {
        FileContent = fileContent;
        TypeName = typeName;
        if (fileName == "")
        {

            FileName = $"{ConvertToTypescriptFileNameConvention(typeName)}.model.ts";
        }
    }

    public static string ConvertToTypescriptFileNameConvention(string typeName)
    {
        return Regex.Replace(typeName, "([a-z](?=[A-Z])|[A-Z](?=[A-Z][a-z]))", "$1-").ToLower();
    }

}