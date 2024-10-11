using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ITBees.ApiToTypescriptGenerator.Services
{
    public class TypeScriptServiceGenerator
    {
        // This method accepts the list of generated model types to consider
        public Dictionary<string, string> GenerateServices(List<Type> generatedModelTypes)
        {
            var services = new Dictionary<string, string>();

            // Filter types to include only the generated models
            var relevantTypes = generatedModelTypes
                .Where(t => t.Name.EndsWith("Vm") || t.Name.EndsWith("Im") || t.Name.EndsWith("Um") || t.Name.EndsWith("Dm"))
                .ToList();

            // Group types by base name
            var groupedTypes = relevantTypes
                .GroupBy(t => GetBaseName(t.Name));

            foreach (var group in groupedTypes)
            {
                var entityName = group.Key;
                var entityTypes = group.ToList();
                var serviceCode = GenerateService(entityName, entityTypes);
                services.Add(entityName, serviceCode);
            }

            return services;
        }

        private string GenerateService(string entityName, List<Type> types)
        {
            // Determine which methods to include
            bool hasVm = types.Any(t => t.Name == entityName + "Vm");
            bool hasIm = types.Any(t => t.Name == entityName + "Im");
            bool hasUm = types.Any(t => t.Name == entityName + "Um");
            bool hasDm = types.Any(t => t.Name == entityName + "Dm");

            // Begin generating the TypeScript code
            var sb = new StringBuilder();

            // Write import statements
            sb.AppendLine("import { Injectable } from '@angular/core';");
            sb.AppendLine("import { HttpClient, HttpHeaders } from '@angular/common/http';");
            sb.AppendLine("import { Observable } from 'rxjs';");
            sb.AppendLine("import { environment } from '../environments/environment';");

            // Import the models
            if (hasVm)
                sb.AppendLine($"import {{ I{entityName}Vm }} from '../models/{ToKebabCase(entityName)}-vm.model';");
            if (hasIm)
                sb.AppendLine($"import {{ I{entityName}Im }} from '../models/{ToKebabCase(entityName)}-im.model';");
            if (hasUm)
                sb.AppendLine($"import {{ I{entityName}Um }} from '../models/{ToKebabCase(entityName)}-um.model';");
            if (hasDm)
                sb.AppendLine($"import {{ I{entityName}Dm }} from '../models/{ToKebabCase(entityName)}-dm.model';");

            // Begin the service class
            sb.AppendLine("");
            sb.AppendLine("@Injectable({");
            sb.AppendLine("  providedIn: 'root'");
            sb.AppendLine("})");
            sb.AppendLine($"export class {entityName}Service {{");
            sb.AppendLine($"  private baseUrl = environment.webApiUrl + '/{ToKebabCase(entityName)}';");
            sb.AppendLine("");
            sb.AppendLine("  constructor(private http: HttpClient) { }");
            sb.AppendLine("");

            // Include methods
            if (hasVm)
            {
                // Generate GET method
                sb.AppendLine($"  get{entityName}(id: string): Observable<I{entityName}Vm> {{");
                sb.AppendLine("    const headers = this.createHeaders();");
                sb.AppendLine($"    return this.http.get<I{entityName}Vm>(`${{this.baseUrl}}/${{id}}`, {{ headers }});");
                sb.AppendLine("  }");
                sb.AppendLine("");
            }

            if (hasIm)
            {
                // Generate POST method
                sb.AppendLine($"  create{entityName}(model: I{entityName}Im): Observable<I{entityName}Vm> {{");
                sb.AppendLine("    const headers = this.createHeaders();");
                sb.AppendLine($"    return this.http.post<I{entityName}Vm>(this.baseUrl, model, {{ headers }});");
                sb.AppendLine("  }");
                sb.AppendLine("");
            }

            if (hasUm)
            {
                // Generate PUT method
                sb.AppendLine($"  update{entityName}(id: string, model: I{entityName}Um): Observable<void> {{");
                sb.AppendLine("    const headers = this.createHeaders();");
                sb.AppendLine($"    return this.http.put<void>(`${{this.baseUrl}}/${{id}}`, model, {{ headers }});");
                sb.AppendLine("  }");
                sb.AppendLine("");
            }

            if (hasDm)
            {
                // Generate DELETE method
                sb.AppendLine($"  delete{entityName}(id: string): Observable<void> {{");
                sb.AppendLine("    const headers = this.createHeaders();");
                sb.AppendLine($"    return this.http.delete<void>(`${{this.baseUrl}}/${{id}}`, {{ headers }});");
                sb.AppendLine("  }");
                sb.AppendLine("");
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

        private string GetBaseName(string typeName)
        {
            if (typeName.EndsWith("Vm") || typeName.EndsWith("Im") || typeName.EndsWith("Um") || typeName.EndsWith("Dm"))
            {
                return typeName.Substring(0, typeName.Length - 2);
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
