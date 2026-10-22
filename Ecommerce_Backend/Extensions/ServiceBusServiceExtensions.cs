using Azure.Messaging.ServiceBus;
using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Messaging;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class ServiceBusServiceExtensions
    {
        public static IServiceCollection AddServiceBusMessaging(this IServiceCollection services, IConfiguration configuration, AppRoles roles)
        {
            services.AddOptions<ServiceBusOptions>()
                .Bind(configuration.GetSection(ServiceBusOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddSingleton(sp =>
                new ServiceBusClient(sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.ConnectionString));

            services.AddKeyedSingleton<ServiceBusSender>(KeyedService.AnyKey, (sp, key) =>
                sp.GetRequiredService<ServiceBusClient>().CreateSender((string)key!));

            services.AddSingleton<IOrderEventPublisher>(sp =>
            {
                var queueName = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.QueueName;
                return new ServiceBusOrderEventPublisher(sp.GetRequiredKeyedService<ServiceBusSender>(queueName));
            });

            if (roles.ConsumerEnabled)
            {
                services.AddHostedService<OrderProcessingService>();
            }

            return services;
        }
    }
}
