using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ecommerce_Backend.HealthChecks
{
    public class ServiceBusHealthCheck : IHealthCheck
    {
        private readonly ServiceBusClient _client;
        private readonly string _queueName;

        public ServiceBusHealthCheck(ServiceBusClient client, string queueName)
        {
            _client = client;
            _queueName = queueName;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var sender = _client.CreateSender(_queueName);
            try
            {
                using var batch = await sender.CreateMessageBatchAsync(cancellationToken).AsTask().WaitAsync(cancellationToken);
                return HealthCheckResult.Healthy($"Queue '{_queueName}' is reachable.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Queue '{_queueName}' is not reachable.",
                    ex);
            }
            finally
            {
                _ = sender.DisposeAsync().AsTask();
            }
        }
    }
}
