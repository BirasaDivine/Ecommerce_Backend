using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    public class ServiceBusOptions
    {
        public static readonly string Path = "ServiceBus";

        [Required(ErrorMessage = "ServiceBus:QueueName must be set.")]
        public string QueueName { get; set; } = string.Empty;

        [Required(ErrorMessage = "ServiceBus:ConnectionString must be set (e.g. via user secrets or the ServiceBus__ConnectionString environment variable).")]
        public string ConnectionString { get; set; } = string.Empty;
    }
}
