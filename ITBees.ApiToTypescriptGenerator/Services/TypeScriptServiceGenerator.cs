using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ITBees.ApiToTypescriptGenerator.Services
{
    public class TypeScriptServiceGenerator
    {
        public Dictionary<string, string> GenerateServices(List<ControllerInfo> controllers)
        {
            var services = new Dictionary<string, string>();

            foreach (var controller in controllers)
            {
                var serviceCode = GenerateService(controller);
                services.Add(controller.ControllerName, serviceCode);
            }

            return services;
        }

        private string GenerateService(ControllerInfo controller)
        {
            var sb = new StringBuilder();

            var entityName = controller.ControllerName.Replace("Controller", "Service");
            var endpointName = controller.EndpointName;

            // Write import statements
            sb.AppendLine("import { Injectable } from '@angular/core';");
            sb.AppendLine("import { HttpClient, HttpHeaders } from '@angular/common/http';");
            sb.AppendLine("import { Observable } from 'rxjs';");
            sb.AppendLine("import { environment } from '../environments/environment';");

            // Import the models used in actions
            var modelImports = new HashSet<string>();

            foreach (var action in controller.Actions)
            {
                if (action.ReturnType != null)
                {
                    var modelName = GetInterfaceName(action.ReturnType);
                    modelImports.Add(modelName);
                }
                if (action.ParameterType != null)
                {
                    var modelName = GetInterfaceName(action.ParameterType);
                    modelImports.Add(modelName);
                }
            }

            foreach (var modelName in modelImports)
            {
                sb.AppendLine($"import {{ I{modelName} }} from '../models/{ToKebabCase(modelName)}.model';");
            }

            // Begin the service class
            sb.AppendLine("");
            sb.AppendLine("@Injectable({");
            sb.AppendLine("  providedIn: 'root'");
            sb.AppendLine("})");
            sb.AppendLine($"export class {entityName} {{");
            sb.AppendLine($"  private baseUrl = environment.webApiUrl + '/{endpointName}';");
            sb.AppendLine("");
            sb.AppendLine("  constructor(private http: HttpClient) { }");
            sb.AppendLine("");

            // Generate methods for actions
            foreach (var action in controller.Actions)
            {
                var methodName = action.ActionName.Substring(0, 1).ToLower() + action.ActionName.Substring(1);

                var returnType = action.ReturnType != null ? $"I{GetInterfaceName(action.ReturnType)}" : "void";

                var parameterDeclaration = "";
                var parameterUsage = "";
                if (action.ParameterType != null)
                {
                    var parameterTypeName = $"I{GetInterfaceName(action.ParameterType)}";
                    parameterDeclaration = $"model: {parameterTypeName}";
                    parameterUsage = "model";
                }

                // Generate method based on HTTP method
                switch (action.HttpMethod.ToUpper())
                {
                    case "GET":
                        sb.AppendLine($"  {methodName}(id: string): Observable<{returnType}> {{");
                        sb.AppendLine("    const headers = this.createHeaders();");
                        sb.AppendLine($"    return this.http.get<{returnType}>(`${{this.baseUrl}}/${{id}}`, {{ headers }});");
                        sb.AppendLine("  }");
                        sb.AppendLine("");
                        break;
                    case "POST":
                        sb.AppendLine($"  {methodName}({parameterDeclaration}): Observable<{returnType}> {{");
                        sb.AppendLine("    const headers = this.createHeaders();");
                        sb.AppendLine($"    return this.http.post<{returnType}>(this.baseUrl, {parameterUsage}, {{ headers }});");
                        sb.AppendLine("  }");
                        sb.AppendLine("");
                        break;
                    case "PUT":
                        sb.AppendLine($"  {methodName}(id: string, {parameterDeclaration}): Observable<{returnType}> {{");
                        sb.AppendLine("    const headers = this.createHeaders();");
                        sb.AppendLine($"    return this.http.put<{returnType}>(`${{this.baseUrl}}/${{id}}`, {parameterUsage}, {{ headers }});");
                        sb.AppendLine("  }");
                        sb.AppendLine("");
                        break;
                    case "DELETE":
                        sb.AppendLine($"  {methodName}(id: string): Observable<{returnType}> {{");
                        sb.AppendLine("    const headers = this.createHeaders();");
                        sb.AppendLine($"    return this.http.delete<{returnType}>(`${{this.baseUrl}}/${{id}}`, {{ headers }});");
                        sb.AppendLine("  }");
                        sb.AppendLine("");
                        break;
                    default:
                        // Handle other HTTP methods if needed
                        break;
                }
            }

            // Include method to create headers with JWT token
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

        private string GetInterfaceName(Type type)
        {
            var typeName = type.Name;
            if (type.IsGenericType)
            {
                var genericArguments = type.GetGenericArguments();
                typeName = typeName.Substring(0, typeName.IndexOf('`'));
                var typeArgumentNames = genericArguments.Select(t => t.Name);
                typeName += string.Join("", typeArgumentNames);
            }
            return typeName;
        }

        private string ToKebabCase(string input)
        {
            // Convert PascalCase to kebab-case
            return System.Text.RegularExpressions.Regex.Replace(input, "(\\B[A-Z])", "-$1").ToLower();
        }
    }

}
