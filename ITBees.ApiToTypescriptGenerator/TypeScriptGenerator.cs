using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using InheritedMapper;
using ITBees.ApiToTypescriptGenerator.Services.Models;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGenerator
    {
        public TypeScriptGeneratedModels Generate(string viewModelName, TypeScriptGeneratedModels x, bool skipChildGeneration, Type[] genericTypeArguments = null)
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

                    if (type.IsAbstract)
                    {
                        type = BaseClassHelper.GetAllDerivedClassesFromBaseClass(type).First();
                    }

                    break;
                }

                if (type == null)
                {
                    throw new Exception($"Type {viewModelName} not found.");
                }

                // Handle generic types
                string interfaceName = RemoveViewModelDecorator(type.Name);
                if (type.IsGenericTypeDefinition)
                {
                    if (genericTypeArguments != null)
                    {
                        // Create a closed generic type with the provided generic type arguments
                        type = type.MakeGenericType(genericTypeArguments);
                        interfaceName = RemoveViewModelDecorator(type.Name);
                        if (interfaceName.Contains('`'))
                        {
                            interfaceName = interfaceName.Substring(0, interfaceName.IndexOf('`'));
                        }
                        var typeArgumentNames = genericTypeArguments.Select(t => RemoveViewModelDecorator(t.Name));
                        interfaceName += string.Join("", typeArgumentNames);
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

                    // Handle nullable types
                    bool isNullable = IsNullableType(propertyType);
                    propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                    // Handle primitive types
                    if (IsPrimitiveType(propertyType))
                    {
                        sb.AppendLine($"    {GetTypescriptPropertyLine(pi, isNullable)};");
                        continue;
                    }

                    // Handle enum types
                    if (propertyType.IsEnum)
                    {
                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {propertyType.Name};");
                        GenerateEnumModel(pi, x);
                        continue;
                    }

                    // Handle collections
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

                            
                            Generate(itemType.Name, x, true, itemType.IsGenericType ? itemType.GetGenericArguments() : null);

                            sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{childInterfaceName}[];");

                            childViewModels.Add(childInterfaceName);
                        }
                        continue;
                    }

                    // Handle complex types
                    if (propertyType.IsClass)
                    {
                        var childInterfaceName = GetInterfaceName(propertyType);


                        Generate(propertyType.Name, x, true, propertyType.IsGenericType ? propertyType.GetGenericArguments() : null);

                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{childInterfaceName};");

                        childViewModels.Add(childInterfaceName);
                        continue;
                    }

                    sb.AppendLine($"    {pi.Name.ToLowerFirstChar()} : *****,");
                }

                var trimmedModel = RemoveLastSpecialSign(sb);
                var typescriptModel = new TypescriptModel(trimmedModel + "\r\n}\r\n", interfaceName);
                x.AddNewObject(typescriptModel);

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
                x.AddNewObject(new TypescriptModel($"\t\t\t>>>> There was a problem while generating class {viewModelName} (property - {currentPi?.Name}), check it manually, error: " + e.Message, viewModelName));
            }
            return x;
        }

        private string GetInterfaceName(Type type)
        {
            var interfaceName = RemoveViewModelDecorator(type.Name);

            if (type.IsGenericType)
            {
                if (interfaceName.Contains('`'))
                {
                    interfaceName = interfaceName.Substring(0, interfaceName.IndexOf('`'));
                }
                var typeArgumentNames = type.GetGenericArguments().Select(t => RemoveViewModelDecorator(t.Name));
                interfaceName += string.Join("", typeArgumentNames);
            }

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
            if (underlyingType == typeof(string))
            {
                return "string";
            }
            else if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short) || underlyingType == typeof(decimal) || underlyingType == typeof(float) || underlyingType == typeof(double))
            {
                return "number";
            }
            else if (underlyingType == typeof(bool))
            {
                return "boolean";
            }
            else if (underlyingType == typeof(Guid))
            {
                return "string";
            }
            else if (underlyingType == typeof(DateTime))
            {
                return "Date";
            }
            else
            {
                return "***undefined***";
            }
        }

        private static string RemoveLastSpecialSign(StringBuilder sb)
        {
            return sb.ToString().TrimEnd('\n').TrimEnd('\r').TrimEnd(',').TrimEnd(';');
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
            typescriptModels.AddNewObject(new TypescriptModel(sb.ToString(), pi.PropertyType.Name));
        }
    }
}
