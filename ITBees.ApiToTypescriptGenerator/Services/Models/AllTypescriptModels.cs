using System.Collections.Generic;

namespace ITBees.ApiToTypescriptGenerator.Services.Models
{
    public class AllTypescriptModels
    {
        public string ModelsContent { get; set; }
        public byte[] ZipArchive { get; set; }
        public List<string> GeneratedModelNames { get; set; }
        public Dictionary<string, string> Services { get; set; }

        public AllTypescriptModels(string modelsContent, byte[] zipArchive, List<string> generatedModelNames, Dictionary<string, string> services)
        {
            ModelsContent = modelsContent;
            ZipArchive = zipArchive;
            GeneratedModelNames = generatedModelNames;
            Services = services;
        }
    }
}