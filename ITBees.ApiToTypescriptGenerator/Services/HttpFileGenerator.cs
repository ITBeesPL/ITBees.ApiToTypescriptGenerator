using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace MyHttpGeneratorExample
{
    /// <summary>
    /// Represents one .http file: its name and generated content.
    /// </summary>
    public class HttpFile
    {
        public string FileName { get; }
        public string FileContent { get; }

        public HttpFile(string fileName, string fileContent)
        {
            FileName = fileName;
            FileContent = fileContent;
        }
    }

    /// <summary>
    /// Main generator that scans an assembly, finds controllers and their routes, 
    /// and produces .http snippet files with example requests.
    /// </summary>
    public class HttpSnippetGenerator
    {
        /// <summary>
        /// Generates a list of .http files for all controllers in the provided assembly.
        /// </summary>
        /// <param name="assembly">Assembly containing your controllers.</param>
        /// <returns>List of HttpFile with file name and content for each controller.</returns>
        public List<HttpFile> GenerateHttpFiles(Assembly assembly)
        {
            // Find all controller types that are not abstract and inherit from ControllerBase (or your custom base).
            var controllers = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
                .ToList();

            var result = new List<HttpFile>();

            foreach (var controllerType in controllers)
            {
                // Generate .http content for that controller
                var content = BuildHttpFileContentForController(controllerType);
                // By default let's name the file "ControllerName.http"
                var fileName = $"{controllerType.Name}.http";

                result.Add(new HttpFile(fileName, content));
            }

            return result;
        }

        /// <summary>
        /// Creates the .http file content for a single controller by scanning all its actions.
        /// </summary>
        private string BuildHttpFileContentForController(Type controllerType)
        {
            var sb = new StringBuilder();

            // Extract the base route from the [Route] attribute on the controller, if present.
            var baseRoute = GetControllerRoute(controllerType);

            // We'll reflect over all public methods that have Http method attributes 
            // (like [HttpGet], [HttpPost], [HttpPut], [HttpDelete], etc.).
            var actionMethods = controllerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.CustomAttributes.Any(a => IsHttpMethodAttribute(a.AttributeType)))
                .ToList();

            // For each action, generate an .http snippet
            foreach (var method in actionMethods)
            {
                BuildSnippetForAction(method, baseRoute, sb);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the .http snippet for a single action method, appending to the StringBuilder.
        /// </summary>
        private void BuildSnippetForAction(MethodInfo method, string baseRoute, StringBuilder sb)
        {
            // Identify which HTTP method is used (GET, POST, etc.)
            var httpMethodAttr = method
                .GetCustomAttributes()
                .FirstOrDefault(a => IsHttpMethodAttribute(a.GetType()));

            if (httpMethodAttr == null)
                return; // Should not happen, but just in case

            var httpMethod = ResolveHttpMethod(httpMethodAttr.GetType());
            
            // Extract the method-level route from e.g. [HttpGet("xyz")], if any
            var methodRoute = GetMethodRoute(httpMethodAttr);

            // Combine the base controller route with method route
            var combinedRoute = CombineRoutes(baseRoute, methodRoute, method.DeclaringType?.Name);

            // Build final path (including potential query string if GET/DELETE)
            var finalPath = BuildPathWithQueryIfNeeded(httpMethod, method, combinedRoute);

            // Start building snippet
            sb.AppendLine("###");
            // To print "{{OperatorApi_HostAddress}}" literally, we need double-escaping in an interpolated string:
            sb.AppendLine($"{httpMethod} {{{{OperatorApi_HostAddress}}}}/{finalPath}");

            // If it's a "login" POST, let's show a specialized snippet
            if (httpMethod == "POST" && methodRoute?.ToLower() == "login")
            {
                sb.AppendLine("Accept: text/plain");
                sb.AppendLine("Content-Type: application/json");
                sb.AppendLine();
                sb.AppendLine("{");
                sb.AppendLine("  \"username\": \"{{adminLogin}}\",");
                sb.AppendLine("  \"password\": \"{{adminPass}}\",");
                sb.AppendLine("  \"language\": \"pl\"");
                sb.AppendLine("}");
                sb.AppendLine();
                return;
            }

            // Otherwise, typical snippet
            sb.AppendLine("Accept: application/json");
            sb.AppendLine("Content-Type: application/json");
            // If you want "bearer {{value}}" specifically:
            sb.AppendLine("Authorization: bearer {{value}}");
            sb.AppendLine();

            // If GET or DELETE, typically we skip the body. 
            // If POST, PUT or PATCH, let's generate JSON from [FromBody] parameter if available.
            if (httpMethod == "POST" || httpMethod == "PUT" || httpMethod == "PATCH")
            {
                var fromBodyParam = method.GetParameters()
                    .FirstOrDefault(p => p.GetCustomAttribute<FromBodyAttribute>() != null);
                
                if (fromBodyParam != null)
                {
                    // Generate a sample JSON object from the parameter type
                    var jsonSample = GenerateJsonForType(fromBodyParam.ParameterType, 0);
                    sb.AppendLine(jsonSample);
                }
                else
                {
                    // Fallback if we don't see a [FromBody] param
                    sb.AppendLine("{");
                    sb.AppendLine("  \"sampleProperty\": \"sampleValue\"");
                    sb.AppendLine("}");
                }
            }

            sb.AppendLine(); // blank line after each snippet
        }

        /// <summary>
        /// If the method is GET or DELETE, we append query parameters for all method parameters
        /// that are NOT from route or body. This is a naive approach assuming they come from query.
        /// </summary>
        private string BuildPathWithQueryIfNeeded(string httpMethod, MethodInfo method, string route)
        {
            // For GET or DELETE, let's gather all parameters that are not from route or body.
            if (httpMethod == "GET" || httpMethod == "DELETE")
            {
                var parameters = method.GetParameters();

                var queryParams = new List<string>();

                foreach (var param in parameters)
                {
                    // If the parameter is decorated with [FromBody], skip it
                    if (param.GetCustomAttribute<FromBodyAttribute>() != null) 
                        continue;

                    // If it's in the route, skip
                    if (IsRouteParameter(method, param))
                        continue;

                    // Otherwise we assume it's from query.
                    var sampleValue = GenerateSimpleQueryValue(param.ParameterType, param.Name);
                    // E.g. param.Name = "page" -> "page=1"
                    queryParams.Add($"{param.Name}={sampleValue}");
                }

                if (queryParams.Any())
                {
                    return route + "?" + string.Join("&", queryParams);
                }
            }

            return route;
        }

        /// <summary>
        /// Checks if the parameter name is used in the route as a {placeholder}, or if it has [FromRoute].
        /// If so, we skip adding it to the query string.
        /// </summary>
        private bool IsRouteParameter(MethodInfo method, ParameterInfo param)
        {
            // If there's an explicit [FromRoute], definitely skip
            if (param.GetCustomAttribute<FromRouteAttribute>() != null)
                return true;

            // Otherwise, check if the route template has something like "{parkingGuid}"
            var httpAttr = method.GetCustomAttributes().FirstOrDefault(a => IsHttpMethodAttribute(a.GetType()));
            if (httpAttr != null)
            {
                var methodRoute = GetMethodRoute(httpAttr);
                if (!string.IsNullOrEmpty(methodRoute))
                {
                    if (methodRoute.Contains("{" + param.Name + "}", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Generate a naive sample value for query usage, with special logic for certain parameter names:
        /// - page -> 1
        /// - pageSize -> 25
        /// - sortColumn -> Id
        /// - sortOrder -> Descending
        /// otherwise tries a general fallback.
        /// </summary>
        private string GenerateSimpleQueryValue(Type type, string paramName)
        {
            // Special case by paramName (lowercased for convenience):
            switch (paramName.ToLower())
            {
                case "page":
                    return "1"; // override
                case "pagesize":
                    return "25"; // override
                case "sortcolumn":
                    return "Id"; // override
                case "sortorder":
                    return "Descending"; // override
            }

            // If it's not one of those special param names, do default logic
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string)) return "stringValue";
            if (type == typeof(Guid)) return "00000000-0000-0000-0000-000000000000";
            if (type == typeof(bool)) return "false";
            if (type.IsEnum)
            {
                var firstName = Enum.GetNames(type).FirstOrDefault();
                return firstName ?? "EnumValue";
            }
            if (type == typeof(DateTime)) return "2024-01-01T00:00:00";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)
                || type == typeof(decimal) || type == typeof(float) || type == typeof(double))
            {
                return "0";
            }
            // fallback
            return "stringValue";
        }

        /// <summary>
        /// Recursively builds a simple JSON sample (as a string) for a given type. 
        /// This is a naive approach - feel free to improve for complex scenarios.
        /// </summary>
        private string GenerateJsonForType(Type type, int depth)
        {
            // Safety check to avoid too deep recursion
            if (depth > 5)
            {
                return "{ \"_recursiveLimit\": true }";
            }

            // If it's a simple type, return a single property example
            if (IsSimpleType(type))
            {
                return GenerateSimpleTypeValue(type);
            }

            // If it's e.g. a class with properties, let's build object structure
            var sb = new StringBuilder();
            sb.AppendLine("{");

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.CanRead && p.CanWrite) // only "real" settable props
                            .ToList();

            for (int i = 0; i < props.Count; i++)
            {
                var prop = props[i];
                var propName = prop.Name.Substring(0, 1).ToLower() + prop.Name.Substring(1);
                var comma = (i == props.Count - 1) ? "" : ","; 

                // If this property is also complex, we recurse
                if (!IsSimpleType(prop.PropertyType))
                {
                    var nestedJson = GenerateJsonForType(prop.PropertyType, depth + 1);
                    // Indent nested
                    var nestedIndented = IndentJson(nestedJson, 2);
                    sb.AppendLine($"  \"{propName}\": {nestedIndented}{comma}");
                }
                else
                {
                    var val = GenerateSimpleTypeValue(prop.PropertyType);
                    sb.AppendLine($"  \"{propName}\": {val}{comma}");
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Returns a naive JSON representation of a simple type, 
        /// e.g. 0 for int, "" for string, "2024-01-01T00:00:00" for DateTime, etc.
        /// </summary>
        private string GenerateSimpleTypeValue(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string)) return "\"stringValue\"";
            if (type == typeof(Guid)) return "\"00000000-0000-0000-0000-000000000000\"";
            if (type == typeof(bool)) return "false";
            if (type.IsEnum) return $"\"{Enum.GetNames(type).FirstOrDefault() ?? "EnumValue"}\"";
            if (type == typeof(DateTime)) return "\"2024-01-01T00:00:00\"";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)
                || type == typeof(decimal) || type == typeof(float) || type == typeof(double))
            {
                return "0";
            }
            // fallback
            return "\"\"";
        }

        /// <summary>
        /// Quick check if a type is "simple" (primitive, string, DateTime, decimal, etc.).
        /// </summary>
        private bool IsSimpleType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive
                   || type.IsEnum
                   || type == typeof(string)
                   || type == typeof(decimal)
                   || type == typeof(DateTime)
                   || type == typeof(Guid);
        }

        /// <summary>
        /// Utility to indent JSON lines with a specified number of spaces.
        /// </summary>
        private string IndentJson(string json, int spaces)
        {
            var prefix = new string(' ', spaces);
            var lines = json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var indentedLines = lines.Select(line => prefix + line);
            return string.Join(Environment.NewLine, indentedLines);
        }

        /// <summary>
        /// Returns e.g. "api/[controller]" from the [Route] attribute on the class if present.
        /// If not found, uses the controller's name (minus "Controller" suffix).
        /// </summary>
        private string GetControllerRoute(Type controllerType)
        {
            var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            if (routeAttr != null && !string.IsNullOrWhiteSpace(routeAttr.Template))
            {
                // e.g. "api/[controller]"
                return routeAttr.Template.TrimStart('/');
            }

            // Fallback if no [Route(...)]: just use the class name minus "Controller"
            var name = controllerType.Name;
            if (name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 10);

            return name; 
        }

        /// <summary>
        /// Reads the route from [HttpGet("route")], [HttpPost("route")] etc. If not set, returns empty string.
        /// </summary>
        private string GetMethodRoute(Attribute httpMethodAttribute)
        {
            // We look for a property "Template" on the HttpMethod attribute to see if there's a route.
            var templateProp = httpMethodAttribute
                .GetType()
                .GetProperty("Template", BindingFlags.Public | BindingFlags.Instance);

            if (templateProp == null)
                return string.Empty;

            var val = templateProp.GetValue(httpMethodAttribute) as string;
            return val?.Trim('/') ?? string.Empty;
        }

        /// <summary>
        /// Combines base route (e.g. "api/[controller]") with method route (e.g. "login"), 
        /// and replaces "[controller]" placeholder with a lowercased version of the actual controller name.
        /// </summary>
        private string CombineRoutes(string baseRoute, string methodRoute, string controllerName)
        {
            if (string.IsNullOrWhiteSpace(controllerName))
                controllerName = "UnknownController";

            // Remove the "Controller" suffix if present, to get e.g. "User" from "UserController"
            if (controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
            {
                controllerName = controllerName.Substring(0, controllerName.Length - 10);
            }

            // For clarity let's convert to lowercase to match typical route conventions
            var lowercaseName = controllerName.ToLower();

            var combined = baseRoute.Replace("[controller]", lowercaseName);

            // Now combine with methodRoute. 
            // e.g. "api/user" + "login" => "api/user/login"
            if (!string.IsNullOrEmpty(methodRoute))
            {
                if (!combined.EndsWith("/"))
                    combined += "/";
                combined += methodRoute;
            }

            return combined;
        }

        /// <summary>
        /// Checks if the given attribute type is one of the standard ASP.NET Core HttpMethod attributes: 
        /// HttpGet, HttpPost, HttpPut, HttpDelete, HttpPatch.
        /// </summary>
        private bool IsHttpMethodAttribute(Type type)
        {
            return type == typeof(HttpGetAttribute)
                || type == typeof(HttpPostAttribute)
                || type == typeof(HttpPutAttribute)
                || type == typeof(HttpDeleteAttribute)
                || type == typeof(HttpPatchAttribute);
        }

        /// <summary>
        /// Maps Http method attributes to string names: GET, POST, PUT, DELETE, PATCH.
        /// </summary>
        private string ResolveHttpMethod(Type attributeType)
        {
            if (attributeType == typeof(HttpGetAttribute)) return "GET";
            if (attributeType == typeof(HttpPostAttribute)) return "POST";
            if (attributeType == typeof(HttpPutAttribute)) return "PUT";
            if (attributeType == typeof(HttpDeleteAttribute)) return "DELETE";
            if (attributeType == typeof(HttpPatchAttribute)) return "PATCH";
            // fallback
            return "GET";
        }
    }
}
