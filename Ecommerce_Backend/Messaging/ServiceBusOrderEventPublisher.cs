using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Ecommerce_Backend.Configuration;

namespace Ecommerce_Backend.Messaging
{
    public class ServiceBusOrderEventPublisher : IOrderEventPublisher
    {
        private readonly ServiceBusSender _sender;

        public ServiceBusOrderEventPublisher(ServiceBusClient client, IOptions<ServiceBusOptions> options)
        {
            _sender = client.CreateSender(options.Value.QueueName);
        }

        public async Task PublishOrderPlacedAsync(OrderPlacedMessage message, CancellationToken cancellationToken = default)
        {
            var body = JsonSerializer.Serialize(message);
            var serviceBusMessage = new ServiceBusMessage(body)
            {
                MessageId = message.OrderId.ToString(),
                SessionId = message.Sku
            };

            await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);
        }
    }
}
