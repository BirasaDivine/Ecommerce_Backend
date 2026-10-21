namespace Ecommerce_Backend.Configuration
{
    public class ServiceBusOptions
    {
        public string QueueName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
    }
}
