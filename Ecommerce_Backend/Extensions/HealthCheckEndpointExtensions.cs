using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ecommerce_Backend.Extensions
{
    public static class HealthCheckEndpointExtensions
    {
        public static WebApplication MapAppHealthChecks(this WebApplication app)
        {
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains(HealthCheckTags.Live),
                ResponseWriter = WriteResponseAsync
            }).AllowAnonymous();

            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
                ResponseWriter = WriteResponseAsync
            }).AllowAnonymous();

            return app;
        }

        // Status per check, but never exception messages: these endpoints are anonymous
        // and a driver error can include server names or connection details.
        private static Task WriteResponseAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";

            var body = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        durationMs = entry.Value.Duration.TotalMilliseconds
                    })
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }
    }
}
