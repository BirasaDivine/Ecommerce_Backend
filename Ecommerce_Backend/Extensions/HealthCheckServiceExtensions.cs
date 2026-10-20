using Azure.Messaging.ServiceBus;
using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Data;
using Ecommerce_Backend.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class HealthCheckTags
    {
        // Live: the process is up. Ready: its dependencies are reachable too.
        public const string Live = "live";
        public const string Ready = "ready";
    }

    public static class HealthCheckServiceExtensions
    {
        // Must be called after AddAppDatabase and AddServiceBusMessaging, whose
        // DbContext and keyed ServiceBusSender the readiness checks resolve.
        public static IServiceCollection AddAppHealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<HealthCheckSettingsOptions>()
                .Bind(configuration.GetSection(HealthCheckSettingsOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { HealthCheckTags.Live })
                .AddDbContextCheck<AppDbContext>("database", tags: new[] { HealthCheckTags.Ready })
                .Add(new HealthCheckRegistration(
                    "servicebus-order-queue",
                    sp =>
                    {
                        var queueName = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.QueueName;
                        return new ServiceBusHealthCheck(sp.GetRequiredKeyedService<ServiceBusSender>(queueName));
                    },
                    failureStatus: null,
                    tags: new[] { HealthCheckTags.Ready }));

            // Apply the configured timeout to every check that didn't set its own.
            services.AddOptions<HealthCheckServiceOptions>()
                .PostConfigure<IOptions<HealthCheckSettingsOptions>>((options, settings) =>
                {
                    var timeout = TimeSpan.FromSeconds(settings.Value.TimeoutSeconds);
                    foreach (var registration in options.Registrations)
                    {
                        if (registration.Timeout == Timeout.InfiniteTimeSpan)
                        {
                            registration.Timeout = timeout;
                        }
                    }
                });

            return services;
        }
    }
}
