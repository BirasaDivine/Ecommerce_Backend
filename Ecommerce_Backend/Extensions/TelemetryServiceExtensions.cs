using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Telemetry;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ecommerce_Backend.Extensions
{
    public static class TelemetryServiceExtensions
    {
        private const string HealthPathPrefix = "/health";

        // Registers traces, metrics and logs for whichever role this process runs. The providers are
        // configured from IOptions when they are built, so settings can come from environment
        // variables (Telemetry__SamplingRatio, Telemetry__Exporter, ...) or from tests.
        public static IServiceCollection AddAppTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<TelemetryOptions>()
                .Bind(configuration.GetSection(TelemetryOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.Configure<OpenTelemetryLoggerOptions>(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
            });

            services.AddOpenTelemetry()
                .WithTracing(tracing => tracing
                    // Health checks would drown out real traffic, so they are not traced.
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPathPrefix))
                    .AddHttpClientInstrumentation()
                    // SQL text is recorded; parameter values are not (SetDbQueryParameters stays off).
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddSource(TelemetryNames.ActivitySourceName))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(TelemetryNames.MeterName))
                .WithLogging();

            services.AddSingleton<OrderMetrics>();
            services.AddHostedService<OrderMetricsInitializer>();

            services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
            {
                var options = GetOptions(sp);
                builder.SetResourceBuilder(CreateResource(sp));
                builder.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.SamplingRatio)));
                switch (options.Exporter)
                {
                    case TelemetryExporter.Console:
                        builder.AddConsoleExporter();
                        break;
                    case TelemetryExporter.Otlp:
                        builder.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
                        break;
                }
            });

            services.ConfigureOpenTelemetryMeterProvider((sp, builder) =>
            {
                var options = GetOptions(sp);
                var intervalMilliseconds = options.MetricExportIntervalSeconds * 1000;
                builder.SetResourceBuilder(CreateResource(sp));
                switch (options.Exporter)
                {
                    case TelemetryExporter.Console:
                        builder.AddConsoleExporter((_, reader) =>
                            reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = intervalMilliseconds);
                        break;
                    case TelemetryExporter.Otlp:
                        builder.AddOtlpExporter((exporter, reader) =>
                        {
                            exporter.Endpoint = new Uri(options.OtlpEndpoint);
                            reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = intervalMilliseconds;
                        });
                        break;
                }
            });

            services.ConfigureOpenTelemetryLoggerProvider((sp, builder) =>
            {
                var options = GetOptions(sp);
                builder.SetResourceBuilder(CreateResource(sp));
                switch (options.Exporter)
                {
                    case TelemetryExporter.Console:
                        builder.AddConsoleExporter();
                        break;
                    case TelemetryExporter.Otlp:
                        builder.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
                        break;
                }
            });

            return services;
        }

        private static TelemetryOptions GetOptions(IServiceProvider sp) =>
            sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;

        // Every signal carries service.name, service.version (the build's commit) and deployment.environment.
        private static ResourceBuilder CreateResource(IServiceProvider sp)
        {
            var options = GetOptions(sp);
            var roles = sp.GetRequiredService<AppRoles>();
            var hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            var commit = sp.GetRequiredService<IOptions<VersionOptions>>().Value.Commit;

            var serviceName = options.ServiceName
                ?? (roles.ApiEnabled ? TelemetryNames.ApiServiceName : TelemetryNames.ConsumerServiceName);
            var environment = options.Environment ?? hostEnvironment.EnvironmentName.ToLowerInvariant();

            return ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: commit)
                .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", environment) });
        }
    }
}
