using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    public enum TelemetryExporter
    {
        None,
        Console,
        Otlp
    }

    public class TelemetryOptions : IValidatableObject
    {
        public static readonly string Path = "Telemetry";

        public TelemetryExporter Exporter { get; set; } = TelemetryExporter.Console;

        // Where the OpenTelemetry Collector listens (OTLP over gRPC), e.g. http://otel-collector:4317.
        public string OtlpEndpoint { get; set; } = string.Empty;

        // Fraction of traces to keep, 0 to 1. Child spans follow their parent's decision.
        [Range(0.0, 1.0, ErrorMessage = "Telemetry:SamplingRatio must be between 0 and 1.")]
        public double SamplingRatio { get; set; } = 1.0;

        // How often metrics are pushed to the exporter.
        [Range(1, 300, ErrorMessage = "Telemetry:MetricExportIntervalSeconds must be between 1 and 300.")]
        public int MetricExportIntervalSeconds { get; set; } = 10;

        // Overrides the role-based default (inventory-api or order-consumer).
        public string? ServiceName { get; set; }

        // Overrides the hosting environment name for the deployment.environment attribute.
        public string? Environment { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Exporter == TelemetryExporter.Otlp && !Uri.IsWellFormedUriString(OtlpEndpoint, UriKind.Absolute))
            {
                yield return new ValidationResult(
                    "Telemetry:OtlpEndpoint must be an absolute URI when Telemetry:Exporter is Otlp.",
                    new[] { nameof(OtlpEndpoint) });
            }
        }
    }
}
