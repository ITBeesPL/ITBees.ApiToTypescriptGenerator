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
        public TypeScriptGeneratedModels Generate(string viewModelName, TypeScriptGeneratedModels x, bool skipChildGeneration)
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
                Assembly[] asmblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in asmblies)
                {
                    if (viewModelName.Contains("."))
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

                var instance = Activator.CreateInstance(type);
                var interfaceName = RemoveViewModelDecorator(viewModelName);
                sb.AppendLine($"export interface I{interfaceName} {{");

                var childViewModels = new List<Type>();
                foreach (PropertyInfo pi in instance.GetType().GetProperties())
                {
                    currentPi = pi;

                    if (IsPrimitiveType(pi.PropertyType))
                    {
                        var propertyIsNullable = IsNullableType(pi.PropertyType);
                        sb.AppendLine($"    {GetTypescriptPropertyLine(pi, propertyIsNullable)},");
                        continue;
                    }

                    if (pi.PropertyType.IsEnum)
                    {
                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {pi.PropertyType.Name},");
                        GenerateEnumModel(pi, x);
                        continue;
                    }

                    if (IsCollectionType(pi.PropertyType))
                    {
                        var genericType = GetGenericType(pi.PropertyType);
                        if (IsPrimitiveType(genericType))
                        {
                            var typescriptType = GetTypescriptTypeFromType(genericType);
                            sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {typescriptType}[],");
                        }
                        else
                        {
                            var childInterfaceName = RemoveViewModelDecorator(genericType.Name);
                            sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{childInterfaceName}[],");
                            childViewModels.Add(genericType);
                        }
                        continue;
                    }

                    if (pi.PropertyType.IsClass)
                    {
                        var childInterfaceName = RemoveViewModelDecorator(pi.PropertyType.Name);
                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{childInterfaceName},");
                        childViewModels.Add(pi.PropertyType);
                        continue;
                    }

                    sb.AppendLine($"    {pi.Name.ToLowerFirstChar()} : *****,");
                }

                var trimmedModel = RemoveLastSpecialSign(sb);
                var typescriptModel = new TypescriptModel(trimmedModel + "\r\n}\r\n", viewModelName);
                x.AddNewObject(typescriptModel);

                var sbImports = new StringBuilder();
                foreach (var childViewModel in childViewModels)
                {
                    Generate(childViewModel.Name, x, true);
                    var importLine = $"import {{ I{childViewModel.Name} }} from './{TypeScriptFile.GetTypescriptFileNameWithoutTs(childViewModel.Name)}';";
                    if (!sbImports.ToString().Contains(importLine))
                    {
                        sbImports.AppendLine(importLine);
                    }
                }
                typescriptModel.SetModel(sbImports + typescriptModel.Model);
            }
            catch (Exception e)
            {
                x.AddNewObject(new TypescriptModel($"\t\t\t>>>>there was an problem while generating class {viewModelName} (property - {currentPi?.Name}),  check it manually, error :" + e.Message, viewModelName));

            }
            return x;
        }

        private string RemoveViewModelDecorator(string viewModelName)
        {
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

        private Type GetGenericType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }
            if (type.IsGenericType)
            {
                return type.GetGenericArguments().First();
            }
            return null;
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
            return sb.ToString().TrimEnd('\n').TrimEnd('\r').TrimEnd(',');
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
