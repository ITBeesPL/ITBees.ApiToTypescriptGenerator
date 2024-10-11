using System.Text;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGeneratedModels
    {
        public List<TypescriptModel> GeneratedModels { get; } = new();

        public override string ToString()
        {
            var s = GeneratedModels.Aggregate(new StringBuilder(), (x, y) => x.AppendLine(y.Model)).ToString();
            return s;
        }

        public void AddNewObject(TypescriptModel model)
        {
            if (GeneratedModels.Any(x => x.TypeName == model.TypeName))
                return;

            GeneratedModels.Add(model);
        }
    }
}