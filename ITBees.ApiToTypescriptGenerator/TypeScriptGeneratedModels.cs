using System.Text;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGeneratedModels
    {
        private List<TypescriptModel> GeneratedOjects { get; } = new();
        public override string ToString()
        {
            var s = GeneratedOjects.Aggregate(new StringBuilder(), (x, y) =>  x.AppendLine(y.Model)).ToString();

            return s;
        }

        public void AddNewObject(TypescriptModel model)
        {
            if (GeneratedOjects.Any(x => x.Equals(model)))
                return;

            GeneratedOjects.Add(model);
        }
    }
}