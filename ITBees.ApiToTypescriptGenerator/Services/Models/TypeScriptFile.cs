using System.Text.RegularExpressions;

namespace ITBees.ApiToTypescriptGenerator.Services.Models
{
    public class TypeScriptFile
    {
        public string FileContent { get; }
        public string TypeName { get; }
        public string FileName { get; }

        public TypeScriptFile(string fileContent, string typeName, string fileName = "")
        {
            FileContent = fileContent;
            TypeName = typeName;
            FileName = string.IsNullOrEmpty(fileName) ? GetTypescriptFileName(typeName) : fileName;
        }

        public static string GetTypescriptFileName(string typeName)
        {
            return $"{ConvertToTypescriptFileNameConvention(typeName)}.model.ts";
        }

        public static string GetTypescriptFileNameWithoutTs(string typeName)
        {
            return $"{ConvertToTypescriptFileNameConvention(typeName)}.model";
        }

        public static string ConvertToTypescriptFileNameConvention(string typeName)
        {
            if (typeName.StartsWith("I") && typeName.Length > 1 && char.IsUpper(typeName[1]))
            {
                typeName = typeName.Substring(1);
            }
            return Regex.Replace(typeName, "(\\B[A-Z])", "-$1").ToLower();
        }
    }
}