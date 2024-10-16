using System.Collections.Generic;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGeneratedModels
    {
        public List<TypescriptModel> GeneratedModels { get; } = new List<TypescriptModel>();
        public HashSet<string> RequiredImports { get; } = new HashSet<string>();

        public void AddNewObject(TypescriptModel model)
        {
            if (GeneratedModels.Exists(x => x.TypeName == model.TypeName))
                return;

            GeneratedModels.Add(model);
        }
    }
}