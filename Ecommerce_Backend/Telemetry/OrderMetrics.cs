using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ecommerce_Backend.Data;
using Ecommerce_Backend.Extensions;
using Ecommerce_Backend.Models;
using OpenTelemetry;

namespace Ecommerce_Backend.Telemetry
{
    public static class RejectionReasons
    {
        public const string InsufficientStock = "insufficient_stock";
        public const string VariantNotFound = "variant_not_found";
    }

    // Order counts and timings. Tags are deliberately low-cardinality: never an order id, user id or SKU.
    public sealed class OrderMetrics : IDisposable
    {
        public const string OrdersPlacedName = "orders.placed";
        public const string OrdersProcessedName = "orders.processed";
        public const string ProcessingDurationName = "orders.processing.duration";
        public const string OrdersPendingName = "orders.pending";

        public const string ConfirmedOutcome = "confirmed";
        public const string RejectedOutcome = "rejected";

        private readonly Meter _meter = new(TelemetryNames.MeterName);
        private readonly Counter<long> _placed;
        private readonly Counter<long> _processed;
        private readonly Histogram<double> _processingDuration;
        private readonly IServiceScopeFactory _scopeFactory;

        public OrderMetrics(IServiceScopeFactory scopeFactory, AppRoles roles)
        {
            _scopeFactory = scopeFactory;

            _placed = _meter.CreateCounter<long>(OrdersPlacedName, unit: "{order}",
                description: "Orders accepted by the API.");
            _processed = _meter.CreateCounter<long>(OrdersProcessedName, unit: "{order}",
                description: "Orders resolved by the consumer, by outcome and rejection reason.");
            _processingDuration = _meter.CreateHistogram<double>(ProcessingDurationName, unit: "ms",
                description: "Time from an order being placed (Pending) to its final status.");

            // Reported by the API role, which stays up while the consumer is down: that is exactly
            // when Pending orders pile up.
            if (roles.ApiEnabled)
            {
                _meter.CreateObservableGauge(OrdersPendingName, ObservePendingOrders, unit: "{order}",
                    description: "Orders currently Pending.");
            }
        }

        public void OrderPlaced() => _placed.Add(1);

        public void OrderConfirmed(DateTime orderDateUtc)
        {
            _processed.Add(1, new KeyValuePair<string, object?>("outcome", ConfirmedOutcome));
            RecordDuration(ConfirmedOutcome, orderDateUtc);
        }

        public void OrderRejected(string reason, DateTime orderDateUtc)
        {
            _processed.Add(1,
                new KeyValuePair<string, object?>("outcome", RejectedOutcome),
                new KeyValuePair<string, object?>("reason", reason));
            RecordDuration(RejectedOutcome, orderDateUtc);
        }

        private void RecordDuration(string outcome, DateTime orderDateUtc)
        {
            var elapsed = DateTime.UtcNow - orderDateUtc;
            _processingDuration.Record(Math.Max(0, elapsed.TotalMilliseconds),
                new KeyValuePair<string, object?>("outcome", outcome));
        }

        private IEnumerable<Measurement<long>> ObservePendingOrders()
        {
            long pending;
            try
            {
                // A background query, not part of any request: keep it out of the traces.
                using var suppress = SuppressInstrumentationScope.Begin();
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                pending = context.Orders.Count(o => o.Status == OrderStatus.Pending);
            }
            catch (Exception)
            {
                // A database outage must not break metric collection; skip this reading.
                return Array.Empty<Measurement<long>>();
            }

            return new[] { new Measurement<long>(pending) };
        }

        public void Dispose() => _meter.Dispose();
    }

    // Creates OrderMetrics at start-up so the Pending gauge is reported before the first order.
    public sealed class OrderMetricsInitializer : IHostedService
    {
        public OrderMetricsInitializer(OrderMetrics metrics)
        {
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
