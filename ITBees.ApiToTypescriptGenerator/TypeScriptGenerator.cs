using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ITBees.ApiToTypescriptGenerator.Services.Models;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGenerator
    {
        public TypeScriptGeneratedModels Generate(Type type, TypeScriptGeneratedModels generatedModels, bool skipChildGeneration)
        {
            PropertyInfo currentPi = null;
            try
            {
                if (type == null)
                {
                    throw new Exception($"Type is null.");
                }

                if (type.Name == "FileContentResult")
                {
                    return null;
                }

                if (type.Name == "IActionResult" || type.Name.StartsWith("ActionResult"))
                {
                    return generatedModels;
                }

                if (type.IsEnum)
                {
                    GenerateEnumModel(type, generatedModels);
                    return generatedModels;
                }

                var sb = new StringBuilder();

                string interfaceName = GetInterfaceName(type);

                sb.AppendLine($"export interface {interfaceName} {{");

                var properties = type.GetProperties();

                var requiredImports = new HashSet<string>();

                foreach (PropertyInfo pi in properties)
                {
                    currentPi = pi;

                    Type propertyType = pi.PropertyType;

                    bool isNullable = IsNullableType(propertyType);
                    propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                    var tsType = GetTypescriptTypeFromType(propertyType, generatedModels, requiredImports);

                    var nullableSign = isNullable ? "?" : "";
                    sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}{nullableSign}: {tsType};");
                }

                sb.AppendLine("}");

                var typescriptModel = new TypescriptModel(sb.ToString(), interfaceName, type);
                typescriptModel.AddRequiredImports(requiredImports);
                generatedModels.AddNewObject(typescriptModel);

                var sbImports = new StringBuilder();
                foreach (var childInterfaceName in typescriptModel.RequiredImports)
                {
                    if (childInterfaceName != interfaceName)
                    {
                        var importName = childInterfaceName;
                        var fileName = TypeScriptFile.GetTypescriptFileNameWithoutTs(importName);
                        sbImports.AppendLine($"import {{ {importName} }} from './{fileName}';");
                    }
                }

                typescriptModel.SetModel(sbImports + typescriptModel.Model);
            }
            catch (Exception e)
            {
                generatedModels.AddNewObject(new TypescriptModel($"\t\t\t>>>> An error occurred while generating class {type.Name} (property - {currentPi?.Name}), check manually, error: " + e.Message, type.Name, null));
            }
            return generatedModels;
        }

        private string GetInterfaceName(Type type)
        {
            var typeName = type.Name;

            if (type.IsGenericType)
            {
                var baseName = typeName.Contains('`') ? typeName.Substring(0, typeName.IndexOf('`')) : typeName;

                if (baseName.StartsWith("I") && baseName.Length > 1 && char.IsUpper(baseName[1]))
                {
                    baseName = baseName.Substring(1);
                }

                var genericArgs = type.GetGenericArguments();
                var genericArgNames = string.Join("", genericArgs.Select(arg =>
                {
                    var argName = GetInterfaceName(arg);
                    if (argName.StartsWith("I") && argName.Length > 1 && char.IsUpper(argName[1]))
                    {
                        argName = argName.Substring(1);
                    }
                    return argName;
                }));

                typeName = $"I{baseName}{genericArgNames}";
            }
            else
            {
                if (typeName.StartsWith("I") && typeName.Length > 1 && char.IsUpper(typeName[1]))
                {
                    typeName = typeName;
                }
                else
                {
                    typeName = $"I{typeName}";
                }
            }

            return typeName;
        }

        private bool IsNullableType(Type type)
        {
            if (!type.IsValueType)
            {
                return true;
            }
            else
            {
                return Nullable.GetUnderlyingType(type) != null;
            }
        }

        private bool IsPrimitiveType(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.IsPrimitive
                || underlyingType == typeof(string)
                || underlyingType == typeof(Guid)
                || underlyingType == typeof(DateTime)
                || underlyingType == typeof(decimal);
        }

        private bool IsCollectionType(Type type)
        {
            if (type == typeof(string))
            {
                return false;
            }

            if (type.IsArray)
            {
                return true;
            }

            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IEnumerable<>) ||
                    genericTypeDefinition == typeof(ICollection<>) ||
                    genericTypeDefinition == typeof(IList<>) ||
                    genericTypeDefinition == typeof(List<>))
                {
                    return true;
                }
            }

            if (typeof(IEnumerable<>).IsAssignableFrom(type))
            {
                return true;
            }

            return false;
        }

        private string GetTypescriptTypeFromType(Type type, TypeScriptGeneratedModels generatedModels, HashSet<string> requiredImports)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (IsPrimitiveType(underlyingType))
            {
                if (underlyingType == typeof(string) || underlyingType == typeof(Guid))
                    return "string";
                if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short) ||
                    underlyingType == typeof(decimal) || underlyingType == typeof(float) || underlyingType == typeof(double))
                    return "number";
                if (underlyingType == typeof(bool))
                    return "boolean";
                if (underlyingType == typeof(DateTime))
                    return "Date";
            }

            if (underlyingType.IsEnum)
            {
                GenerateEnumModel(underlyingType, generatedModels);
                requiredImports.Add(underlyingType.Name);
                return underlyingType.Name;
            }

            if (IsCollectionType(underlyingType))
            {
                var itemType = GetCollectionItemType(underlyingType);
                var tsItemType = GetTypescriptTypeFromType(itemType, generatedModels, requiredImports);
                return $"{tsItemType}[]";
            }

            if (underlyingType.IsGenericType)
            {
                var genericTypeDefinition = underlyingType.GetGenericTypeDefinition();

                if (genericTypeDefinition == typeof(Nullable<>))
                {
                    var innerType = underlyingType.GetGenericArguments().First();
                    return $"{GetTypescriptTypeFromType(innerType, generatedModels, requiredImports)} | null";
                }

                var interfaceName = GetInterfaceName(underlyingType);

                foreach (var arg in underlyingType.GetGenericArguments())
                {
                    Generate(arg, generatedModels, true);
                }

                Generate(underlyingType, generatedModels, true);

                if (!IsCollectionType(underlyingType))
                {
                    requiredImports.Add(interfaceName);
                }
                return interfaceName;
            }

            if (underlyingType.IsClass && underlyingType != typeof(string))
            {
                var classTypeName = GetInterfaceName(underlyingType);
                Generate(underlyingType, generatedModels, true);
                if (!IsCollectionType(underlyingType))
                {
                    requiredImports.Add(classTypeName);
                }
                return classTypeName;
            }

            return "any";
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

        private void GenerateEnumModel(Type enumType, TypeScriptGeneratedModels typescriptModels)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"export enum {enumType.Name} {{");
            var enumValues = Enum.GetNames(enumType);
            foreach (var value in enumValues)
            {
                sb.AppendLine($"    {value} = '{value}',");
            }
            sb.AppendLine("}");
            typescriptModels.AddNewObject(new TypescriptModel(sb.ToString(), enumType.Name, enumType));
        }
    }
}
