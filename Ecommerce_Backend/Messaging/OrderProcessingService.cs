using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Messaging
{
    public class OrderProcessingService : BackgroundService
    {
        private readonly ServiceBusClient _client;
        private readonly ServiceBusOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderProcessingService> _logger;
        private ServiceBusSessionProcessor? _processor;

        public OrderProcessingService(
            ServiceBusClient client,
            IOptions<ServiceBusOptions> options,
            IServiceScopeFactory scopeFactory,
            ILogger<OrderProcessingService> logger)
        {
            _client = client;
            _options = options.Value;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _processor = _client.CreateSessionProcessor(_options.QueueName, new ServiceBusSessionProcessorOptions
            {
                MaxConcurrentSessions = 5,
                MaxConcurrentCallsPerSession = 1,
                AutoCompleteMessages = false
            });

            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            await _processor.StopProcessingAsync(CancellationToken.None);
        }

        private async Task ProcessMessageAsync(ProcessSessionMessageEventArgs args)
        {
            var message = JsonSerializer.Deserialize<OrderPlacedMessage>(args.Message.Body.ToString());
            if (message == null)
            {
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId);
            if (order == null || order.Status != OrderStatus.Pending)
            {
                // Already processed - a redelivery after a restart/crash. Idempotent no-op.
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var variant = await context.Variants.FirstOrDefaultAsync(v => v.Id == message.VariantId);
            if (variant == null || variant.Quantity < message.Quantity)
            {
                order.Status = OrderStatus.Rejected;
                order.RejectionReason = "Insufficient stock";
                await context.SaveChangesAsync();
                _logger.LogInformation("Order {OrderId} rejected: insufficient stock.", order.Id);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            variant.Quantity -= message.Quantity;
            order.Status = OrderStatus.Confirmed;
            await context.SaveChangesAsync();

            // Only complete after the state change is durably committed - if the
            // process crashes before this point, the lock expires and Service Bus
            // redelivers the message; the idempotency check above handles the retry.
            await args.CompleteMessageAsync(args.Message);
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            _logger.LogError(args.Exception, "Error processing Service Bus message.");
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_processor != null)
            {
                await _processor.StopProcessingAsync(cancellationToken);
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
