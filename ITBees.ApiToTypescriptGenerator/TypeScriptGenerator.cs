using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ITBees.ApiToTypescriptGenerator.Services.Models;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGenerator
    {
        public TypeScriptGeneratedModels Generate(string viewModelName, TypeScriptGeneratedModels generatedModels, bool skipChildGeneration, Type[] genericTypeArguments = null)
        {
            PropertyInfo currentPi = null;
            try
            {
                if (viewModelName == "FileContentResult")
                {
                    return null;
                }
                var sb = new StringBuilder();
                Type type = null;
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in assemblies)
                {
                    if (viewModelName.Contains("`"))
                    {
                        type = assembly.GetTypes().FirstOrDefault(t => t.Name == viewModelName);
                    }
                    else if (viewModelName.Contains("."))
                    {
                        type = assembly.GetTypes().FirstOrDefault(x => x.FullName == viewModelName);
                    }
                    else
                    {
                        type = assembly.GetTypes().FirstOrDefault(x => x.Name == viewModelName);
                    }

                    if (type == null)
                        continue;

                    break;
                }

                if (type == null)
                {
                    throw new Exception($"Type {viewModelName} not found.");
                }

                string interfaceName = GetInterfaceName(type);
                if (type.IsGenericTypeDefinition)
                {
                    if (genericTypeArguments != null)
                    {
                        type = type.MakeGenericType(genericTypeArguments);
                        interfaceName = GetInterfaceName(type);
                    }
                    else
                    {
                        throw new Exception($"Generic type arguments required for type {viewModelName}.");
                    }
                }

                sb.AppendLine($"export interface I{interfaceName} {{");

                var childViewModels = new HashSet<string>();

                var properties = type.GetProperties();

                foreach (PropertyInfo pi in properties)
                {
                    currentPi = pi;

                    Type propertyType = pi.PropertyType;

                    bool isNullable = IsNullableType(propertyType);
                    propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                    if (IsPrimitiveType(propertyType))
                    {
                        sb.AppendLine($"    {GetTypescriptPropertyLine(pi, isNullable)};");
                        continue;
                    }

                    if (propertyType.IsEnum)
                    {
                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {propertyType.Name};");
                        GenerateEnumModel(pi, generatedModels);
                        continue;
                    }

                    if (IsCollectionType(propertyType))
                    {
                        Type itemType = GetCollectionItemType(propertyType);

                        if (IsPrimitiveType(itemType))
                        {
                            var tsType = GetTypescriptTypeFromType(itemType);
                            sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {tsType}[];");
                        }
                        else
                        {
                            var childInterfaceName = GetInterfaceName(itemType);

                            Generate(itemType.Name, generatedModels, true, itemType.IsGenericType ? itemType.GetGenericArguments() : null);

                            sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{childInterfaceName}[];");

                            childViewModels.Add(childInterfaceName);
                        }
                        continue;
                    }

                    if (propertyType.IsClass && propertyType != typeof(string))
                    {
                        var childInterfaceName = GetInterfaceName(propertyType);

                        Generate(propertyType.Name, generatedModels, true, propertyType.IsGenericType ? propertyType.GetGenericArguments() : null);

                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{childInterfaceName};");

                        childViewModels.Add(childInterfaceName);
                        continue;
                    }

                    sb.AppendLine($"    {pi.Name.ToLowerFirstChar()} : any;");
                }

                sb.AppendLine("}");

                var typescriptModel = new TypescriptModel(sb.ToString(), interfaceName, type);
                generatedModels.AddNewObject(typescriptModel);

                var sbImports = new StringBuilder();
                foreach (var childInterfaceName in childViewModels)
                {
                    var importLine = $"import {{ I{childInterfaceName} }} from './{TypeScriptFile.GetTypescriptFileNameWithoutTs(childInterfaceName)}';";
                    if (!sbImports.ToString().Contains(importLine) && childInterfaceName != interfaceName)
                    {
                        sbImports.AppendLine(importLine);
                    }
                }

                typescriptModel.SetModel(sbImports + typescriptModel.Model);
            }
            catch (Exception e)
            {
                generatedModels.AddNewObject(new TypescriptModel($"\t\t\t>>>> An error occurred while generating class {viewModelName} (property - {currentPi?.Name}), check manually, error: " + e.Message, viewModelName, null));
            }
            return generatedModels;
        }

        private string GetInterfaceName(Type type)
        {
            var interfaceName = type.Name;

            if (type.IsGenericType)
            {
                if (interfaceName.Contains('`'))
                {
                    interfaceName = interfaceName.Substring(0, interfaceName.IndexOf('`'));
                }
                var typeArgumentNames = type.GetGenericArguments().Select(t => GetInterfaceName(t));
                interfaceName += string.Join("", typeArgumentNames);
            }

            return interfaceName;
        }

        private bool IsNullableType(Type type)
        {
            return Nullable.GetUnderlyingType(type) != null;
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

        private string GetTypescriptPropertyLine(PropertyInfo pi, bool nullable)
        {
            var typescriptType = GetTypescriptTypeFromType(pi.PropertyType);
            var nullableSign = nullable ? "?" : "";
            return $"{pi.Name.ToLowerFirstChar()}{nullableSign}: {typescriptType}";
        }

        private string GetTypescriptTypeFromType(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (IsPrimitiveType(underlyingType))
            {
                if (underlyingType == typeof(string) || underlyingType == typeof(Guid))
                    return "string";
                if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short) || underlyingType == typeof(decimal) || underlyingType == typeof(float) || underlyingType == typeof(double))
                    return "number";
                if (underlyingType == typeof(bool))
                    return "boolean";
                if (underlyingType == typeof(DateTime))
                    return "Date";
            }

            if (IsCollectionType(underlyingType))
            {
                var itemType = GetCollectionItemType(underlyingType);
                var tsItemType = GetTypescriptTypeFromType(itemType);
                return $"{tsItemType}[]";
            }

            if (underlyingType.IsClass)
            {
                return $"I{GetInterfaceName(underlyingType)}";
            }

            return "any";
        }

        private void GenerateEnumModel(PropertyInfo pi, TypeScriptGeneratedModels typescriptModels)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"export enum {pi.PropertyType.Name} {{");
            var enumValues = Enum.GetNames(pi.PropertyType);
            foreach (var value in enumValues)
            {
                sb.AppendLine($"    {value},");
            }
            sb.AppendLine("}\r\n");
            typescriptModels.AddNewObject(new TypescriptModel(sb.ToString(), pi.PropertyType.Name, pi.PropertyType));
        }
    }
}
