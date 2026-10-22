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

        // Registers traces, metrics and logs for whichever role this process runs.
        //
        // Which exporter to use is read here, while services are being registered, because the OTLP
        // exporter registers services of its own and that is not allowed once the provider is built.
        // The sampling ratio, service name and environment are read later from IOptions.
        public static IServiceCollection AddAppTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<TelemetryOptions>()
                .Bind(configuration.GetSection(TelemetryOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            var startup = configuration.GetSection(TelemetryOptions.Path).Get<TelemetryOptions>() ?? new TelemetryOptions();
            var exporter = ResolveExporter(startup);
            var metricIntervalMilliseconds = Math.Clamp(startup.MetricExportIntervalSeconds, 1, 300) * 1000;

            services.Configure<OpenTelemetryLoggerOptions>(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
            });

            services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    tracing
                        // Health checks would drown out real traffic, so they are not traced.
                        .AddAspNetCoreInstrumentation(options =>
                            options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPathPrefix))
                        .AddHttpClientInstrumentation()
                        // SQL text is recorded; parameter values are not (SetDbQueryParameters stays off).
                        .AddEntityFrameworkCoreInstrumentation()
                        .AddSource(TelemetryNames.ActivitySourceName);

                    switch (exporter)
                    {
                        case TelemetryExporter.Console:
                            tracing.AddConsoleExporter();
                            break;
                        case TelemetryExporter.Otlp:
                            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(startup.OtlpEndpoint));
                            break;
                    }
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddMeter(TelemetryNames.MeterName);

                    switch (exporter)
                    {
                        case TelemetryExporter.Console:
                            metrics.AddConsoleExporter((_, reader) =>
                                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = metricIntervalMilliseconds);
                            break;
                        case TelemetryExporter.Otlp:
                            metrics.AddOtlpExporter((otlp, reader) =>
                            {
                                otlp.Endpoint = new Uri(startup.OtlpEndpoint);
                                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = metricIntervalMilliseconds;
                            });
                            break;
                    }
                })
                .WithLogging(logging =>
                {
                    switch (exporter)
                    {
                        case TelemetryExporter.Console:
                            logging.AddConsoleExporter();
                            break;
                        case TelemetryExporter.Otlp:
                            logging.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(startup.OtlpEndpoint));
                            break;
                    }
                });

            services.AddSingleton<OrderMetrics>();
            services.AddHostedService<OrderMetricsInitializer>();

            services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
            {
                var options = GetOptions(sp);
                builder.SetResourceBuilder(CreateResource(sp));
                builder.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.SamplingRatio)));
            });
            services.ConfigureOpenTelemetryMeterProvider((sp, builder) => builder.SetResourceBuilder(CreateResource(sp)));
            services.ConfigureOpenTelemetryLoggerProvider((sp, builder) => builder.SetResourceBuilder(CreateResource(sp)));

            return services;
        }

        // An OTLP exporter with an unusable endpoint is skipped here; the options validation then
        // stops the app at start-up with a clear message.
        private static TelemetryExporter ResolveExporter(TelemetryOptions options) =>
            options.Exporter == TelemetryExporter.Otlp && !Uri.IsWellFormedUriString(options.OtlpEndpoint, UriKind.Absolute)
                ? TelemetryExporter.None
                : options.Exporter;

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
