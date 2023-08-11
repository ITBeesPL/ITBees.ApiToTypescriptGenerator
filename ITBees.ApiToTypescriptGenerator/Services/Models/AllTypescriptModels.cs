namespace ITBees.ApiToTypescriptGenerator.Services.Models;

public class AllTypescriptModels
{
    public string AllModelsInOneString { get; }
    public byte[] ZipArchive { get; }

    public AllTypescriptModels(string allModelsInOneString, byte[] zipArchive)
    {
        AllModelsInOneString = allModelsInOneString;
        ZipArchive = zipArchive;
    }
}