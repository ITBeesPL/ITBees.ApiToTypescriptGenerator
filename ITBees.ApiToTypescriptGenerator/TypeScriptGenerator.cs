﻿using System;
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

                var sb = new StringBuilder();

                string interfaceName = GetInterfaceName(type);

                sb.AppendLine($"export interface I{interfaceName} {{");

                var properties = type.GetProperties();

                foreach (PropertyInfo pi in properties)
                {
                    currentPi = pi;

                    Type propertyType = pi.PropertyType;

                    bool isNullable = IsNullableType(propertyType);
                    propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                    var tsType = GetTypescriptTypeFromType(propertyType, generatedModels);

                    var nullableSign = isNullable ? "?" : "";
                    sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}{nullableSign}: {tsType};");
                }

                sb.AppendLine("}");

                var typescriptModel = new TypescriptModel(sb.ToString(), interfaceName, type);
                generatedModels.AddNewObject(typescriptModel);

                var sbImports = new StringBuilder();
                foreach (var childInterfaceName in generatedModels.RequiredImports)
                {
                    if (childInterfaceName != interfaceName)
                    {
                        sbImports.AppendLine($"import {{ I{childInterfaceName} }} from './{TypeScriptFile.GetTypescriptFileNameWithoutTs(childInterfaceName)}';");
                    }
                }

                typescriptModel.SetModel(sbImports + typescriptModel.Model);
                generatedModels.RequiredImports.Clear();
            }
            catch (Exception e)
            {
                generatedModels.AddNewObject(new TypescriptModel($"\t\t\t>>>> An error occurred while generating class {type.Name} (property - {currentPi?.Name}), check manually, error: " + e.Message, type.Name, null));
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

                var genericArgs = type.GetGenericArguments();
                var genericArgNames = string.Join("", genericArgs.Select(arg => GetInterfaceName(arg)));
                interfaceName += genericArgNames;
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

            return false;
        }

        private string GetTypescriptTypeFromType(Type type, TypeScriptGeneratedModels generatedModels)
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

            if (IsCollectionType(underlyingType))
            {
                var itemType = GetCollectionItemType(underlyingType);
                var tsItemType = GetTypescriptTypeFromType(itemType, generatedModels);
                return $"{tsItemType}[]";
            }

            if (underlyingType.IsGenericType)
            {
                var genericTypeDefinition = underlyingType.GetGenericTypeDefinition();

                if (genericTypeDefinition == typeof(Nullable<>))
                {
                    var innerType = underlyingType.GetGenericArguments().First();
                    return $"{GetTypescriptTypeFromType(innerType, generatedModels)} | null";
                }

                // For other generic types, we can generate a concrete interface
                var concreteTypeName = GetInterfaceName(underlyingType);
                Generate(underlyingType, generatedModels, true);
                if (!IsCollectionType(underlyingType))
                {
                    generatedModels.RequiredImports.Add(concreteTypeName);
                }
                return $"I{concreteTypeName}";
            }

            if (underlyingType.IsClass && underlyingType != typeof(string))
            {
                var classTypeName = GetInterfaceName(underlyingType);
                Generate(underlyingType, generatedModels, true);
                if (!IsCollectionType(underlyingType))
                {
                    generatedModels.RequiredImports.Add(classTypeName);
                }
                return $"I{classTypeName}";
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
