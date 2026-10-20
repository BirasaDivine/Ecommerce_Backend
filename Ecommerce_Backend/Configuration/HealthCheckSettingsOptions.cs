using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    // Named to avoid clashing with ASP.NET Core's endpoint-level HealthCheckOptions.
    public class HealthCheckSettingsOptions
    {
        public static readonly string Path = "HealthChecks";

        // Per-check timeout; a dependency that doesn't answer within this window is
        // reported Unhealthy instead of hanging the readiness probe.
        [Range(1, 120, ErrorMessage = "HealthChecks:TimeoutSeconds must be between 1 and 120.")]
        public int TimeoutSeconds { get; set; } = 5;
    }
}
