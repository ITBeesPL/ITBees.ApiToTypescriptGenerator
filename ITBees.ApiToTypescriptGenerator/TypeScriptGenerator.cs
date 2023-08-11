using System.Collections;
using System.Reflection;
using System.Text;
using InheritedMapper;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGenerator
    {
        public TypeScriptGeneratedModels Generate(string viewModelName, TypeScriptGeneratedModels x)
        {
            PropertyInfo currentPi = null;
            try
            {
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
                    
                    var format = string.Join(";", assembly.GetTypes().Select(x=>x.FullName));
                    Console.WriteLine(format);
                    if (type == null)
                        continue;

                    if (type.IsAbstract)
                    {
                        type = BaseClassHelper.GetAllDerivedClassesFromBaseClass(type).First();
                    }

                    break;
                }

                var instance = Activator.CreateInstance(type);
                if (viewModelName.Contains("ViewModel") || viewModelName.Contains("UpdateModel") || viewModelName.Contains("InputModel")|| viewModelName.Trim().EndsWith("Dto") )
                {
                    sb.AppendLine($"export interface I{RemoveViewModelDecorator(viewModelName)} {(char)123}");
                }
                else
                {
                    sb.AppendLine($"export interface I{viewModelName} {(char)123}");
                }

                var childViewModels = new List<Type>();
                foreach (PropertyInfo pi in instance.GetType().GetProperties())
                {
                    currentPi = pi;
                    if (pi.PropertyType == typeof(Guid) || pi.PropertyType == typeof(Guid?))
                    {
                        var propertyIsNullable = pi.PropertyType == typeof(Guid?);
                        sb.AppendLine($"    {GetTypescriptTypeFromPrimitive(pi, propertyIsNullable)},");
                        continue;
                    }


                    if (pi.PropertyType == typeof(string))
                    {
                        sb.AppendLine($"    {GetTypescriptTypeFromPrimitive(pi, false)},");
                        continue;
                    }

                    if (pi.PropertyType == typeof(Int32) || pi.PropertyType == typeof(Int32?))
                    {
                        var propertyIsNullable = pi.PropertyType == typeof(Int32?);
                        sb.AppendLine($"    {GetTypescriptTypeFromPrimitive(pi, propertyIsNullable)},");
                        continue;
                    }

                    if (pi.PropertyType == typeof(bool) || pi.PropertyType == typeof(bool?))
                    {
                        var propertyIsNullable = pi.PropertyType == typeof(bool?);
                        sb.AppendLine($"    {GetTypescriptTypeFromPrimitive(pi, propertyIsNullable)},");
                        continue;
                    }

                    if ((pi.PropertyType.IsEnum))
                    {
                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {pi.Name},");
                        GenerateEnumModel(pi, x);
                        continue;
                    }

                    if (pi.PropertyType == typeof(decimal) || pi.PropertyType == typeof(decimal?))
                    {
                        var propertyIsNullable = pi.PropertyType == typeof(decimal?);
                        sb.AppendLine($"    {GetTypescriptTypeFromPrimitive(pi, propertyIsNullable)},");
                        continue;
                    }

                    if (pi.PropertyType == typeof(DateTime) || pi.PropertyType == typeof(DateTime?))
                    {
                        var propertyIsNullable = pi.PropertyType == typeof(DateTime?);
                        sb.AppendLine($"    {GetTypescriptTypeFromPrimitive(pi, propertyIsNullable)},");
                        continue;
                    }

                    if (pi.PropertyType.IsCollectionType() || pi.PropertyType.GetInterfaces().Contains(typeof(IEnumerable)))
                    {
                        var removeViewModelDecorator = RemoveViewModelDecorator(pi.PropertyType.GetGenericArguments().First().Name);
                        if (pi.PropertyType.GetInterfaces().Contains(typeof(IEnumerable)))
                        {
                            var propertyBaseType = instance.GetType().GetProperty(pi.Name).PropertyType.GetGenericArguments().First();
                            if (propertyBaseType.IsPrimitive || propertyBaseType == typeof(string))
                            {
                                //sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {pi.PropertyType.GetGenericArguments().First().Name.Replace("ViewModel", "")}[],");
                                sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: {removeViewModelDecorator.ToLowerFirstChar()}[],");
                                continue;
                            }
                        }
                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{removeViewModelDecorator.ToLowerFirstChar()}[],");
                        childViewModels.Add(pi.PropertyType.GetGenericArguments().First());
                        continue;
                    }

                    if (pi.PropertyType.IsClass)
                    {
                        sb.AppendLine($"    {pi.Name.ToLowerFirstChar()}: I{RemoveViewModelDecorator(pi.PropertyType.Name)},");
                        childViewModels.Add(pi.PropertyType);
                        continue;
                    }


                    sb.AppendLine($"    {pi.Name.ToLowerFirstChar()} : *****,");
                }

                var trimEnd = RemoveLastSpecialSign(sb);

                x.AddNewObject(new TypescriptModel(trimEnd + "\r\n}\r\n", viewModelName));

                foreach (var childViewModel in childViewModels)
                {
                    Generate(childViewModel.Name, x);
                }
            }
            catch (Exception e)
            {
                x.AddNewObject(new TypescriptModel($"\t\t\t>>>>there was an problem while generating class {viewModelName} (property - {currentPi?.Name}),  check it manually, error :" + e.Message, viewModelName));

            }
            return x;
        }

        private string RemoveViewModelDecorator(string viewModelName)
        {
            return viewModelName.Replace("ViewModel", "").Replace("UpdateModel", "").Replace("InputModel", "").Replace("Dto", "");
        }

        private string GetTypescriptTypeFromPrimitive(PropertyInfo piPropertyType, bool nullable)
        {
            var nullableSign = nullable ? "?" : "";
            var typescriptDefinition = $"{piPropertyType.Name.ToLowerFirstChar()}{nullableSign}";
            switch (piPropertyType.PropertyType)
            {
                case var type when type == typeof(String) :
                    return $"{typescriptDefinition}: string";
                case var type when type == typeof(string):
                    return $"{typescriptDefinition}: string";
                case var type when type == typeof(int) || type == typeof(int?):
                    return $"{typescriptDefinition}: number";
                case var type when type == typeof(Int32) || type == typeof(Int32?):
                    return $"{typescriptDefinition}: number";
                case var type when type == typeof(decimal) || type == typeof(decimal?):
                    return $"{typescriptDefinition}: number";
                case var type when type == typeof(Boolean) || type == typeof(Boolean?):
                    return $"{typescriptDefinition}: bool";
                case var type when type == typeof(Guid) || type == typeof(Guid?):
                    return $"{typescriptDefinition}: string";
                case var type when type == typeof(DateTime) || type == typeof(DateTime?):
                    return $"{typescriptDefinition}: Date";

                default:
                    return $"{piPropertyType.Name.ToLowerFirstChar()}: ***undefined***";
            }
        }

        private static string RemoveLastSpecialSign(StringBuilder sb)
        {
            return sb.ToString().TrimEnd((char)10).TrimEnd((char)13).TrimEnd((char)44);
        }

        private void GenerateEnumModel(PropertyInfo pi, TypeScriptGeneratedModels typescirptTypeScriptGeneratedModels)
        {
            var sb = new StringBuilder();
            sb.AppendLine("export enum " + pi.Name + " " + (char)123);
            foreach (var memberInfo in pi.PropertyType.GetMembers(BindingFlags.Public | BindingFlags.Static))
            {
                sb.AppendLine("\t" + memberInfo.GetMemberValue(pi) + ",");
            }

            var model = RemoveLastSpecialSign(sb) + "\r\n}\r\n";
            typescirptTypeScriptGeneratedModels.AddNewObject(new TypescriptModel(model, pi.Name));
        }
    }

    public static class Extension
    {
        public static bool IsCollectionType(this Type type)
        {
            return typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string);
        }
        public static object GetMemberValue(this MemberInfo member, object forObject)
        {
            switch (member)
            {
                case FieldInfo mfi:
                    return mfi.GetValue(forObject);
                case PropertyInfo mpi:
                    return mpi.GetValue(forObject, null);
                default:
                    throw new ArgumentException("MemberInfo must be of type FieldInfo or PropertyInfo", nameof(member));
            }
        }
    }
}