using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ITBees.ApiToTypescriptGenerator.Services.Models;

namespace ITBees.ApiToTypescriptGenerator.Services
{
    public class TypeScriptServiceGenerator
    {
        public Dictionary<string, string> GenerateServices(List<ServiceMethod> serviceMethods)
        {
            var services = new Dictionary<string, string>();

            var groupedByController = serviceMethods.GroupBy(sm => sm.ControllerName);

            foreach (var controllerGroup in groupedByController)
            {
                var controllerName = controllerGroup.Key;
                var methods = controllerGroup.ToList();

                var serviceCode = GenerateService(controllerName, methods);

                services.Add(controllerName, serviceCode);
            }

            return services;
        }

        private string GenerateService(string controllerName, List<ServiceMethod> methods)
        {
            var sb = new StringBuilder();

            sb.AppendLine("import { Injectable } from '@angular/core';");
            sb.AppendLine("import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';");
            sb.AppendLine("import { Observable } from 'rxjs';");
            sb.AppendLine("import { environment } from '../../app/environments/environments';");

            var modelsToImport = new HashSet<string>();

            foreach (var method in methods)
            {
                var returnTypeName = GetTypeScriptTypeName(method.ReturnType, modelsToImport);

                foreach (var param in method.Parameters)
                {
                    var paramTypeName = GetTypeScriptTypeName(param.ParameterType, modelsToImport);
                }
            }

            // Generate import statements with correct file names
            foreach (var model in modelsToImport)
            {
                var modelNameWithoutI = model.StartsWith("I") ? model.Substring(1) : model;
                var fileName = ToKebabCase(modelNameWithoutI);
                sb.AppendLine($"import {{ {model} }} from '../{fileName}.model';");
            }

            sb.AppendLine("");
            sb.AppendLine("@Injectable({");
            sb.AppendLine("  providedIn: 'root'");
            sb.AppendLine("})");
            sb.AppendLine($"export class {controllerName}Service {{");

            sb.AppendLine($"  private baseUrl = environment.webApiUrl + '/{controllerName}';");
            sb.AppendLine("");

            sb.AppendLine("  constructor(private http: HttpClient) { }");
            sb.AppendLine("");

            foreach (var method in methods)
            {
                var methodName = method.ActionName.ToLowerFirstChar();
                var httpMethod = method.HttpMethod.ToUpper();
                var returnType = GetTypeScriptTypeName(method.ReturnType, modelsToImport);
                var returnTypeString = returnType != null ? $"Observable<{returnType}>" : "Observable<void>";

                var requiredParameters = new List<string>();
                var optionalParameters = new List<string>();

                foreach (var param in method.Parameters)
                {
                    var paramType = GetTypeScriptTypeName(param.ParameterType, modelsToImport);
                    var isOptional = IsNullableType(param.ParameterInfo);
                    var optionalSign = isOptional ? "?" : "";
                    var parameterDeclaration = $"{param.Name}{optionalSign}: {paramType}";

                    if (isOptional)
                    {
                        optionalParameters.Add(parameterDeclaration);
                    }
                    else
                    {
                        requiredParameters.Add(parameterDeclaration);
                    }
                }

                var parametersString = string.Join(", ", requiredParameters.Concat(optionalParameters));

                sb.AppendLine($"  {methodName}({parametersString}): {returnTypeString} {{");
                sb.AppendLine("    const headers = this.createHeaders();");

                if (httpMethod == "GET" || httpMethod == "DELETE")
                {
                    sb.AppendLine("    let params = new HttpParams();");

                    foreach (var param in method.Parameters)
                    {
                        if (!param.FromBody)
                        {
                            var paramName = param.Name;
                            var paramType = GetTypeScriptTypeName(param.ParameterType, modelsToImport);
                            sb.AppendLine($"    if ({paramName} !== undefined && {paramName} !== null) {{");
                            if (paramType == "string")
                            {
                                sb.AppendLine($"      params = params.set('{paramName}', {paramName});");
                            }
                            else
                            {
                                sb.AppendLine($"      params = params.set('{paramName}', {paramName}.toString());");
                            }
                            sb.AppendLine("    }");
                        }
                    }

                    var url = "this.baseUrl";
                    sb.AppendLine($"    return this.http.get<{returnType}>({url}, {{ headers, params }});");
                }
                else if (httpMethod == "POST" || httpMethod == "PUT")
                {
                    var bodyParam = method.Parameters.FirstOrDefault(p => p.FromBody);
                    var url = "this.baseUrl";

                    if (bodyParam != null)
                    {
                        var bodyParamName = bodyParam.Name;
                        sb.AppendLine($"    return this.http.{httpMethod.ToLower()}<{returnType}>({url}, {bodyParamName}, {{ headers }});");
                    }
                    else
                    {
                        sb.AppendLine($"    return this.http.{httpMethod.ToLower()}<{returnType}>({url}, null, {{ headers }});");
                    }
                }

                sb.AppendLine("  }");
                sb.AppendLine("");
            }

            sb.AppendLine("  private createHeaders(): HttpHeaders {");
            sb.AppendLine("    let headers = new HttpHeaders();");
            sb.AppendLine("    const token = localStorage.getItem('token');");
            sb.AppendLine("    if (token) {");
            sb.AppendLine("      headers = headers.set('Authorization', `Bearer ${token}`);");
            sb.AppendLine("    }");
            sb.AppendLine("    return headers;");
            sb.AppendLine("  }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private bool IsNullableType(ParameterInfo parameter)
        {
            if (parameter.ParameterType.IsValueType)
            {
                // Value types are nullable if they are Nullable<T>
                return Nullable.GetUnderlyingType(parameter.ParameterType) != null;
            }
            else
            {
                // Treat reference types as non-nullable
                return false;
            }
        }

        private string GetTypeScriptTypeName(Type type, HashSet<string> modelsToImport)
        {
            if (type == null || type == typeof(void))
                return null;

            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (IsBuiltInType(underlyingType))
            {
                return GetTypeScriptPrimitiveType(underlyingType);
            }

            if (underlyingType.IsEnum)
            {
                var enumName = underlyingType.Name;
                modelsToImport.Add(enumName);
                return enumName;
            }

            if (IsCollectionType(underlyingType))
            {
                var itemType = GetCollectionItemType(underlyingType);
                var tsItemType = GetTypeScriptTypeName(itemType, modelsToImport);
                return $"{tsItemType}[]";
            }

            if (underlyingType.IsGenericType)
            {
                var interfaceName = GetInterfaceName(underlyingType);
                modelsToImport.Add($"I{interfaceName}");
                return $"I{interfaceName}";
            }

            var typeName = $"I{underlyingType.Name}";
            modelsToImport.Add(typeName);
            return typeName;
        }

        private bool IsBuiltInType(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.IsPrimitive
                || underlyingType == typeof(string)
                || underlyingType == typeof(decimal)
                || underlyingType == typeof(DateTime)
                || underlyingType == typeof(Guid);
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

            if (typeof(IEnumerable<>).IsAssignableFrom(type.GetTypeInfo()))
            {
                return true;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                return true;
            }

            return false;
        }

        private Type GetCollectionItemType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }
            if (type.IsGenericType)
            {
                return type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
            }
            return typeof(object);
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

        private string GetTypeScriptPrimitiveType(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (underlyingType == typeof(string) || underlyingType == typeof(Guid))
                return "string";
            if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short) ||
                underlyingType == typeof(decimal) || underlyingType == typeof(float) || underlyingType == typeof(double))
                return "number";
            if (underlyingType == typeof(bool))
                return "boolean";
            if (underlyingType == typeof(DateTime))
                return "Date";
            return "any";
        }

        private string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

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
