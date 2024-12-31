namespace ITBees.ApiToTypescriptGenerator.Services;

public class HttpFile
{
    public string FileName { get; set; }
    public string FileContent { get; set; }

    public HttpFile(string fileName, string fileContent)
    {
        FileName = fileName;
        FileContent = fileContent;
    }
}