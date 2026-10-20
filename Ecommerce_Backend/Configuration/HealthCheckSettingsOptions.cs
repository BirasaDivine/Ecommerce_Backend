using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    public class HealthCheckSettingsOptions
    {
        public static readonly string Path = "HealthChecks";

        [Range(1, 120, ErrorMessage = "HealthChecks:TimeoutSeconds must be between 1 and 120.")]
        public int TimeoutSeconds { get; set; } = 5;
    }
}
