using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace Ecommerce_Backend.Telemetry
{
    // Carries the W3C trace context (traceparent / tracestate) in a message's application properties,
    // so the consumer's work is part of the same trace as the request that placed the order.
    public static class ServiceBusTraceContext
    {
        public static void Inject(Activity? activity, ServiceBusMessage message)
        {
            if (activity == null)
            {
                return;
            }

            DistributedContextPropagator.Current.Inject(
                activity,
                message.ApplicationProperties,
                static (carrier, key, value) => ((IDictionary<string, object>)carrier!)[key] = value);
        }

        public static ActivityContext Extract(ServiceBusReceivedMessage message)
        {
            DistributedContextPropagator.Current.ExtractTraceIdAndState(
                message.ApplicationProperties,
                static (object? carrier, string name, out string? value, out IEnumerable<string>? values) =>
                {
                    values = null;
                    value = carrier is IReadOnlyDictionary<string, object> properties
                            && properties.TryGetValue(name, out var raw)
                        ? raw?.ToString()
                        : null;
                },
                out var traceParent,
                out var traceState);

            return ActivityContext.TryParse(traceParent, traceState, out var context) ? context : default;
        }
    }
}
