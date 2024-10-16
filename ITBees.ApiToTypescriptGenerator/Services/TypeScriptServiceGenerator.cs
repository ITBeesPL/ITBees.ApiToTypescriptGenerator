using System;
using System.Collections.Generic;
using System.Linq;
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
                var returnTypeName = GetTypeScriptTypeName(method.ReturnType);
                if (returnTypeName != null && !IsBuiltInType(method.ReturnType))
                {
                    modelsToImport.Add(GetBaseTypeName(returnTypeName));
                }

                foreach (var param in method.Parameters)
                {
                    var paramTypeName = GetTypeScriptTypeName(param.ParameterType);
                    if (paramTypeName != null && !IsBuiltInType(param.ParameterType))
                    {
                        modelsToImport.Add(GetBaseTypeName(paramTypeName));
                    }
                }
            }

            // Generate import statements with correct file names
            foreach (var model in modelsToImport)
            {
                var modelNameWithoutI = model.StartsWith("I") ? model.Substring(1) : model;
                sb.AppendLine($"import {{ {model} }} from '../{ToKebabCase(modelNameWithoutI)}.model';");
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
                var returnType = GetTypeScriptTypeName(method.ReturnType);
                var returnTypeString = returnType != null ? $"Observable<{returnType}>" : "Observable<void>";

                var parametersList = new List<string>();
                foreach (var param in method.Parameters)
                {
                    var paramType = GetTypeScriptTypeName(param.ParameterType);
                    parametersList.Add($"{param.Name}: {paramType}");
                }
                var parametersString = string.Join(", ", parametersList);

                sb.AppendLine($"  {methodName}({parametersString}): {returnTypeString} {{");
                sb.AppendLine("    const headers = this.createHeaders();");

                if (httpMethod == "GET" || httpMethod == "DELETE")
                {
                    sb.AppendLine("    let params = new HttpParams();");

                    foreach (var param in method.Parameters)
                    {
                        if (!param.FromBody)
                        {
                            sb.AppendLine($"    if ({param.Name} !== undefined && {param.Name} !== null) {{");
                            sb.AppendLine($"      params = params.set('{param.Name}', {param.Name}.toString());");
                            sb.AppendLine("    }");
                        }
                    }

                    var url = "this.baseUrl";

                    if (httpMethod == "DELETE")
                    {
                        var bodyParam = method.Parameters.FirstOrDefault(p => p.FromBody);
                        if (bodyParam != null)
                        {
                            var bodyParamName = bodyParam.Name;
                            sb.AppendLine($"    return this.http.delete<{returnType}>({url}, {{ headers, params, body: {bodyParamName} }});");
                        }
                        else
                        {
                            sb.AppendLine($"    return this.http.delete<{returnType}>({url}, {{ headers, params }});");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"    return this.http.get<{returnType}>({url}, {{ headers, params }});");
                    }
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

        private string GetBaseTypeName(string typeName)
        {
            if (typeName.EndsWith("[]"))
            {
                return typeName.TrimEnd('[', ']');
            }
            return typeName;
        }

        private string GetTypeScriptTypeName(Type type)
        {
            if (type == null || type == typeof(void))
                return null;

            if (IsBuiltInType(type))
            {
                return GetTypeScriptPrimitiveType(type);
            }

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var tsElementType = GetTypeScriptTypeName(elementType);
                return $"{tsElementType}[]";
            }

            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();

                if (genericTypeDefinition == typeof(List<>) || genericTypeDefinition == typeof(IEnumerable<>))
                {
                    var itemType = type.GetGenericArguments()[0];
                    var tsItemType = GetTypeScriptTypeName(itemType);
                    return $"{tsItemType}[]";
                }

                if (genericTypeDefinition == typeof(Nullable<>))
                {
                    var underlyingType = Nullable.GetUnderlyingType(type);
                    return GetTypeScriptTypeName(underlyingType);
                }

                // Handle other generic types if needed
                return "any";
            }

            return $"I{type.Name}";
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
