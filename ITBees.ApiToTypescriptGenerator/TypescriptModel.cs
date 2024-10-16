using System;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypescriptModel
    {
        public string Model { get; private set; }
        public string TypeName { get; private set; }
        public string ClassType { get; private set; }
        public Type OriginalType { get; private set; }

        public TypescriptModel(string model, string typeName, Type originalType)
        {
            Model = model;
            TypeName = typeName;
            ClassType = typeName;
            OriginalType = originalType;
        }

        public void SetModel(string model)
        {
            Model = model;
        }
    }
}