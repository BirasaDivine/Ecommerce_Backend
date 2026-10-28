using System.Text.Json;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Ecommerce_Backend.Telemetry;

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
            using var activity = AppTelemetry.ActivitySource.StartActivity("order-placed publish", ActivityKind.Producer);
            activity?.SetTag("messaging.system", "servicebus");
            activity?.SetTag("messaging.destination.name", _sender.EntityPath);
            activity?.SetTag("messaging.operation", "publish");
            activity?.SetTag("order.id", message.OrderId);

            var body = JsonSerializer.Serialize(message);
            var serviceBusMessage = new ServiceBusMessage(body)
            {
                MessageId = message.OrderId.ToString(),
                SessionId = message.Sku
            };

            // The consumer continues this trace from the traceparent carried in the message.
            ServiceBusTraceContext.Inject(activity, serviceBusMessage);

            await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);
        }
    }
}
