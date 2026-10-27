using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Messaging
{
    public class OrderProcessingService : BackgroundService
    {
        private readonly ServiceBusClient _client;
        private readonly ServiceBusOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OrderMetrics _metrics;
        private readonly ILogger<OrderProcessingService> _logger;
        private ServiceBusSessionProcessor? _processor;

        public OrderProcessingService(
            ServiceBusClient client,
            IOptions<ServiceBusOptions> options,
            IServiceScopeFactory scopeFactory,
            OrderMetrics metrics,
            ILogger<OrderProcessingService> logger)
        {
            _client = client;
            _options = options.Value;
            _scopeFactory = scopeFactory;
            _metrics = metrics;
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

            try
            {
                await _processor.StartProcessingAsync(stoppingToken);

                try
                {
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
            }
            finally
            {
                await _processor.StopProcessingAsync(CancellationToken.None);
            }
        }

        private async Task ProcessMessageAsync(ProcessSessionMessageEventArgs args)
        {
            // Continue the trace of the request that placed the order (traceparent from the message).
            var parentContext = ServiceBusTraceContext.Extract(args.Message);
            using var processActivity = AppTelemetry.ActivitySource.StartActivity(
                "order-placed process", ActivityKind.Consumer, parentContext);
            processActivity?.SetTag("messaging.system", "servicebus");
            processActivity?.SetTag("messaging.destination.name", _options.QueueName);
            processActivity?.SetTag("messaging.operation", "process");

            try
            {
                await ProcessOrderAsync(args, processActivity);
            }
            catch (Exception ex)
            {
                // Only the exception type: messages can carry data we do not want in a trace.
                processActivity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                throw;
            }
        }

        private async Task ProcessOrderAsync(ProcessSessionMessageEventArgs args, Activity? processActivity)
        {
            var message = JsonSerializer.Deserialize<OrderPlacedMessage>(args.Message.Body.ToString());
            if (message == null)
            {
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            processActivity?.SetTag("order.id", message.OrderId);

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId);
            if (order == null || order.Status != OrderStatus.Pending)
            {
                processActivity?.SetTag("order.outcome", "skipped");
                _logger.LogInformation("Order {OrderId} skipped: not found or already {Status}.", message.OrderId, order?.Status);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            using var stockCheck = AppTelemetry.ActivitySource.StartActivity("order.stock-check");
            stockCheck?.SetTag("order.id", order.Id);
            stockCheck?.SetTag("variant.sku", message.Sku);

            var variant = await context.Variants.FirstOrDefaultAsync(v => v.Id == message.VariantId);
            if (variant == null || variant.Quantity < message.Quantity)
            {
                var reason = variant == null ? RejectionReasons.VariantNotFound : RejectionReasons.InsufficientStock;

                order.Status = OrderStatus.Rejected;
                order.RejectionReason = "Insufficient stock";
                if (!await TrySaveAsync(context, order, args, processActivity))
                {
                    return;
                }

                stockCheck?.SetTag("order.outcome", OrderMetrics.RejectedOutcome);
                stockCheck?.SetTag("order.rejection_reason", reason);
                processActivity?.SetTag("order.outcome", OrderMetrics.RejectedOutcome);
                _metrics.OrderRejected(reason, order.OrderDate);

                _logger.LogInformation("Order {OrderId} rejected: {Reason}.", order.Id, reason);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            variant.Quantity -= message.Quantity;
            order.Status = OrderStatus.Confirmed;
            if (!await TrySaveAsync(context, order, args, processActivity))
            {
                return;
            }

            stockCheck?.SetTag("order.outcome", OrderMetrics.ConfirmedOutcome);
            processActivity?.SetTag("order.outcome", OrderMetrics.ConfirmedOutcome);
            _metrics.OrderConfirmed(order.OrderDate);

            _logger.LogInformation("Order {OrderId} confirmed.", order.Id);
            await args.CompleteMessageAsync(args.Message);
        }

        // Saves the order (and any stock change in the same transaction). Returns false when the buyer
        // cancelled the order after we read it: nothing was written, so the stock is untouched, and the
        // message is completed. Any other concurrency failure is rethrown so Service Bus retries it.
        private async Task<bool> TrySaveAsync(
            AppDbContext context, Order order, ProcessSessionMessageEventArgs args, Activity? processActivity)
        {
            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                var current = await context.Orders.AsNoTracking()
                    .Where(o => o.Id == order.Id)
                    .Select(o => (OrderStatus?)o.Status)
                    .FirstOrDefaultAsync();
                if (current != OrderStatus.Cancelled)
                {
                    throw;
                }

                processActivity?.SetTag("order.outcome", "skipped");
                _logger.LogInformation("Order {OrderId} skipped: cancelled while being processed.", order.Id);
                await args.CompleteMessageAsync(args.Message);
                return false;
            }
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            _logger.LogError(args.Exception, "Error processing Service Bus message.");
            return Task.CompletedTask;
        }
    }
}
