using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Ecommerce_Backend.Messaging
{
    public class ServiceBusOrderEventPublisher : IOrderEventPublisher
    {
        private readonly ServiceBusSender _sender;

        public ServiceBusOrderEventPublisher(ServiceBusSender sender)
        {
            _sender = sender;
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
