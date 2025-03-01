using Microsoft.AspNetCore.SignalR;

namespace ITBees.ApiToTypescriptGenerator.Interfaces;

public interface IHubMethod
{
    string Name { get; }
    Task ExecuteAsync(Hub hub, object payload);
}