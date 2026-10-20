using Azure.Messaging.ServiceBus;
using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Messaging;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class ServiceBusServiceExtensions
    {
        // Registers the Service Bus client, one keyed ServiceBusSender per queue name,
        // the order-placed publisher, and the background consumer.
        public static IServiceCollection AddServiceBusMessaging(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<ServiceBusOptions>()
                .Bind(configuration.GetSection(ServiceBusOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddSingleton(sp =>
                new ServiceBusClient(sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.ConnectionString));

            // Keyed by queue name, so the publisher and the per-queue health check share
            // the same sender (and its open AMQP link) instead of each creating their own.
            services.AddKeyedSingleton<ServiceBusSender>(KeyedService.AnyKey, (sp, key) =>
                sp.GetRequiredService<ServiceBusClient>().CreateSender((string)key!));

            services.AddSingleton<IOrderEventPublisher>(sp =>
            {
                var queueName = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.QueueName;
                return new ServiceBusOrderEventPublisher(sp.GetRequiredKeyedService<ServiceBusSender>(queueName));
            });

            services.AddHostedService<OrderProcessingService>();

            return services;
        }
    }
}
