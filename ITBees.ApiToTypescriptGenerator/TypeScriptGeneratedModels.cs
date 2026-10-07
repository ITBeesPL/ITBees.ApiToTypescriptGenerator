using System;
using System.Collections.Generic;

namespace ITBees.ApiToTypescriptGenerator
{
    public class TypeScriptGeneratedModels
    {
        public List<TypescriptModel> GeneratedModels { get; } = new List<TypescriptModel>();
        public HashSet<string> RequiredImports { get; } = new HashSet<string>();

        // Types generated or still being generated in this run. A type reached again (a reference cycle such as
        // UserAccount.PartnerAccount, or a type shared by many models) is only referenced by its interface name.
        private readonly HashSet<Type> _startedTypes = new HashSet<Type>();

        public void AddNewObject(TypescriptModel model)
        {
            if (GeneratedModels.Exists(x => x.TypeName == model.TypeName))
                return;

            GeneratedModels.Add(model);
        }

        internal bool TryStartGenerating(Type type)
        {
            return _startedTypes.Add(type);
        }
    }
}