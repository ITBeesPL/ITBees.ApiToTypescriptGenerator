namespace ITBees.ApiToTypescriptGenerator
{
    /// <summary>
    /// Switches the generator endpoints (/AllControllersToTypescript, /AngularModels, /TypeScriptModelGenerator).
    /// They are anonymous, because fas-gen calls them without a token, and expensive, so by default they answer only
    /// in the Development environment and return 404 everywhere else.
    /// </summary>
    public class ApiToTypescriptGeneratorOptions
    {
        public const string ConfigurationSection = "ApiToTypescriptGenerator";

        /// <summary>
        /// true - always available, false - always 404. Left null, the value comes from configuration
        /// ("ApiToTypescriptGenerator:EndpointsEnabled", e.g. env variable ApiToTypescriptGenerator__EndpointsEnabled=true),
        /// and when that is not set either, the endpoints are available only in the Development environment.
        /// </summary>
        public bool? EndpointsEnabled { get; set; }
    }
}
