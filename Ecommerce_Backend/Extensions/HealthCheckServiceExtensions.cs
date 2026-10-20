using Azure.Messaging.ServiceBus;
using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class HealthCheckTags
    {
        public const string Live = "live";
        public const string Ready = "ready";
    }

    public static class HealthCheckServiceExtensions
    {
        public static IServiceCollection AddAppHealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<HealthCheckSettingsOptions>()
                .Bind(configuration.GetSection(HealthCheckSettingsOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { HealthCheckTags.Live })
                .AddCheck<DatabaseHealthCheck>("database", tags: new[] { HealthCheckTags.Ready })
                .Add(new HealthCheckRegistration(
                    "servicebus-order-queue",
                    sp =>
                    {
                        var queueName = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.QueueName;
                        return new ServiceBusHealthCheck(sp.GetRequiredService<ServiceBusClient>(), queueName);
                    },
                    failureStatus: null,
                    tags: new[] { HealthCheckTags.Ready }));

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
