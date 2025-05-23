﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ITBees.ApiToTypescriptGenerator.Services
{
    public class TypeScriptServiceGenerator
    {
        public Dictionary<string, string> GenerateServices(List<ServiceMethod> serviceMethods)
        {
            var services = new Dictionary<string, string>();
            var groupedByController = serviceMethods.GroupBy(x => x.ControllerName);

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
            sb.AppendLine("import { Injectable, Inject } from '@angular/core';");
            sb.AppendLine("import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';");
            sb.AppendLine("import { Observable } from 'rxjs';");
            sb.AppendLine("import { API_URL } from '../models/api-url.token';");

            var modelsToImport = new HashSet<string>();
            foreach (var method in methods)
            {
                GetTypeScriptTypeName(method.ReturnType, modelsToImport);
                foreach (var param in method.Parameters)
                    GetTypeScriptTypeName(param.ParameterType, modelsToImport);
            }

            foreach (var model in modelsToImport)
            {
                var modelName = model.StartsWith("I") && model.Length > 1 && char.IsUpper(model[1])
                    ? model[1..]
                    : model;
                sb.AppendLine($"import {{ {model} }} from '../{ToKebabCase(modelName)}.model';");
            }

            sb.AppendLine("@Injectable({ providedIn: 'root' })");
            sb.AppendLine($"export class {controllerName}Service {{");
            sb.AppendLine("  private readonly baseUrl: string;");
            sb.AppendLine("  constructor(private http: HttpClient, @Inject(API_URL) private apiUrl: string) {");
            sb.AppendLine($"    this.baseUrl = `${{this.apiUrl}}/{controllerName}`;");
            sb.AppendLine("  }");

            foreach (var method in methods)
            {
                var httpMethod = method.HttpMethod.ToUpper();
                var methodName = httpMethod.ToLower();
                var returnType = GetTypeScriptTypeName(method.ReturnType, modelsToImport);
                var returnTypeString = returnType != "void" ? $"Observable<{returnType}>" : "Observable<any>";

                var requiredParameters = new List<string>();
                var optionalParameters = new List<string>();
                foreach (var param in method.Parameters)
                {
                    var ts = GetTypeScriptTypeName(param.ParameterType, modelsToImport);
                    var opt = IsNullableType(param.ParameterInfo) ? "?" : "";
                    var decl = $"{param.Name}{opt}: {ts}";
                    if (opt == "?") optionalParameters.Add(decl);
                    else requiredParameters.Add(decl);
                }

                var paramsDecl = string.Join(", ", requiredParameters.Concat(optionalParameters));
                sb.AppendLine($"  {methodName}({paramsDecl}): {returnTypeString} {{");
                sb.AppendLine("    const headers = this.createHeaders();");

                if (httpMethod == "GET" || httpMethod == "DELETE")
                {
                    sb.AppendLine("    let params = new HttpParams();");
                    foreach (var param in method.Parameters)
                    {
                        if (param.FromBody) continue;
                        var name = param.Name;
                        var ts = GetTypeScriptTypeName(param.ParameterType, modelsToImport);
                        sb.AppendLine($"    if ({name} !== undefined && {name} !== null) {{");
                        if (ts.EndsWith("[]"))
                        {
                            sb.AppendLine(
                                $"      {name}.forEach(v => params = params.append('{name}', v.toString()));");
                        }
                        else if (ts == "string" || ts == "number" || ts == "boolean")
                        {
                            sb.AppendLine($"      params = params.set('{name}', {name}.toString());");
                        }
                        else
                        {
                            sb.AppendLine($"      params = params.set('{name}', JSON.stringify({name}));");
                        }

                        sb.AppendLine("    }");
                    }

                    if (httpMethod == "GET")
                        sb.AppendLine($"    return this.http.get<{returnType}>(this.baseUrl, {{ headers, params }});");
                    else
                    {
                        var opts = new List<string> { "headers", "params" };
                        var bodyParam = method.Parameters.FirstOrDefault(x => x.FromBody);
                        if (bodyParam != null) opts.Add($"body: {bodyParam.Name}");
                        sb.AppendLine(
                            $"    return this.http.delete<{returnType}>(this.baseUrl, {{ {string.Join(", ", opts)} }});");
                    }
                }
                else
                {
                    var body = method.Parameters.FirstOrDefault(x => x.FromBody)?.Name ?? "null";
                    sb.AppendLine(
                        $"    return this.http.{httpMethod.ToLower()}<{returnType}>(this.baseUrl, {body}, {{ headers }});");
                }

                sb.AppendLine("  }");
            }

            sb.AppendLine("  private createHeaders(): HttpHeaders {");
            sb.AppendLine("    let headers = new HttpHeaders();");
            sb.AppendLine("    const token = localStorage.getItem('authToken');");
            sb.AppendLine("    if (token) headers = headers.set('Authorization', `Bearer ${token}`);");
            sb.AppendLine("    return headers;");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private bool IsNullableType(ParameterInfo parameter)
        {
            if (parameter?.ParameterType?.IsValueType == true)
            {
                return Nullable.GetUnderlyingType(parameter.ParameterType) != null;
            }

            return false;
        }

        private string GetTypeScriptTypeName(Type type, HashSet<string> modelsToImport)
        {
            if (type == null || type == typeof(void))
            {
                return "void";
            }

            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t.Name == "IActionResult" || t.Name.StartsWith("ActionResult"))
            {
                return "any";
            }

            if (IsBuiltInType(t))
            {
                return GetTypeScriptPrimitiveType(t);
            }

            if (t.IsEnum)
            {
                var e = t.Name;
                modelsToImport.Add(e);
                return e;
            }

            if (IsCollectionType(t))
            {
                var itemType = GetCollectionItemType(t);
                var tsItemType = GetTypeScriptTypeName(itemType, modelsToImport);
                return tsItemType + "[]";
            }

            if (t.IsGenericType)
            {
                var inter = GetInterfaceName(t);
                foreach (var arg in t.GetGenericArguments())
                {
                    GetTypeScriptTypeName(arg, modelsToImport);
                }

                modelsToImport.Add(inter);
                return inter;
            }

            var typeName = GetInterfaceName(t);
            modelsToImport.Add(typeName);
            return typeName;
        }

        private bool IsBuiltInType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t.IsPrimitive
                || t == typeof(string)
                || t == typeof(decimal)
                || t == typeof(DateTime)
                || t == typeof(Guid))
            {
                return true;
            }

            return false;
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
            var n = type.Name;
            if (type.IsGenericType)
            {
                var baseName = n.Contains("`") ? n.Substring(0, n.IndexOf('`')) : n;
                if (baseName.StartsWith("I") && baseName.Length > 1 && char.IsUpper(baseName[1]))
                {
                    baseName = baseName.Substring(1);
                }

                var genArgs = type.GetGenericArguments();
                var appended = string.Join("", genArgs.Select(a =>
                {
                    var argName = GetInterfaceName(a);
                    if (argName.StartsWith("I") && argName.Length > 1 && char.IsUpper(argName[1]))
                    {
                        argName = argName.Substring(1);
                    }

                    return argName;
                }));
                return "I" + baseName + appended;
            }
            else
            {
                if (!(n.StartsWith("I") && n.Length > 1 && char.IsUpper(n[1])))
                {
                    n = "I" + n;
                }
            }

            return n;
        }

        private string GetTypeScriptPrimitiveType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(string) || t == typeof(Guid))
            {
                return "string";
            }

            if (t == typeof(int)
                || t == typeof(long)
                || t == typeof(short)
                || t == typeof(decimal)
                || t == typeof(float)
                || t == typeof(double))
            {
                return "number";
            }

            if (t == typeof(bool))
            {
                return "boolean";
            }

            if (t == typeof(DateTime))
            {
                return "Date";
            }

            return "any";
        }

        private string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

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