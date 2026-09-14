namespace Ecommerce_Backend.Configuration
{
    public class ServiceBusOptions
    {
        public string QueueName { get; set; } = string.Empty;

        // Never committed: set via `dotnet user-secrets` locally, or an
        // environment variable / App Service config when deployed.
        public string ConnectionString { get; set; } = string.Empty;
    }
}
