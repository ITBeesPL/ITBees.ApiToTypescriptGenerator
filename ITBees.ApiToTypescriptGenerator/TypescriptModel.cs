namespace ITBees.ApiToTypescriptGenerator
{
    public class TypescriptModel
    {
        public string Model { get; private set; }
        public string ClassType { get; }

        public TypescriptModel(string model, string classType)
        {
            Model = model;
            ClassType = classType;
        }

        public void SetModel(string newModel)
        {
            Model = newModel;
        }
        public override bool Equals(object obj)
        {
            var sameClassType = ((TypescriptModel)obj).ClassType == this.ClassType;
            var sameModelBody = ((TypescriptModel)obj).Model == this.Model;
            return sameClassType
                   && sameModelBody;
        }
    }
}