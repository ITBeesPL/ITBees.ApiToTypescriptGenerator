using System.Collections;
using System.Reflection;

namespace ITBees.ApiToTypescriptGenerator;

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