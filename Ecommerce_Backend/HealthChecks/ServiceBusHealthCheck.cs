using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ecommerce_Backend.HealthChecks
{
    // Checks a single queue through the same keyed ServiceBusSender the app publishes with.
    // CreateMessageBatchAsync attaches the send link to the queue (without sending anything),
    // so it fails if the broker is unreachable or the queue doesn't exist.
    public class ServiceBusHealthCheck : IHealthCheck
    {
        private readonly ServiceBusSender _sender;

        public ServiceBusHealthCheck(ServiceBusSender sender)
        {
            _sender = sender;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // The SDK doesn't always observe cancellation promptly while it is retrying a
                // connection, so WaitAsync makes sure the configured timeout is honoured.
                using var batch = await _sender.CreateMessageBatchAsync(cancellationToken).AsTask().WaitAsync(cancellationToken);
                return HealthCheckResult.Healthy($"Queue '{_sender.EntityPath}' is reachable.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Queue '{_sender.EntityPath}' is not reachable.",
                    ex);
            }
        }
    }
}
