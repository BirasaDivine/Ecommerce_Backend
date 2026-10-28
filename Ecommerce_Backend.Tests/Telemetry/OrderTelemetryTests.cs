using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Ecommerce_Backend.Tests.Telemetry
{
    [TestFixture]
    public class OrderTelemetryTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private record LoginResponse(string Token);
        private record OrderResponse(int Id, OrderStatus Status);

        // The in-memory exporter is written to from request and consumer threads at once.
        private sealed class ThreadSafeActivities : ICollection<Activity>
        {
            private readonly ConcurrentQueue<Activity> _items = new();
            public int Count => _items.Count;
            public bool IsReadOnly => false;
            public void Add(Activity item) => _items.Enqueue(item);
            public IEnumerator<Activity> GetEnumerator() => _items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(Activity item) => _items.Contains(item);
            public void CopyTo(Activity[] array, int arrayIndex) => _items.ToArray().CopyTo(array, arrayIndex);
            public bool Remove(Activity item) => throw new NotSupportedException();
        }

        private static WebApplicationFactoryScope StartApp(ICollection<Activity> activities, string? samplingRatio = null)
        {
            var factory = new CustomWebApplicationFactory().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    if (samplingRatio != null)
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Telemetry:SamplingRatio"] = samplingRatio });
                    }
                });
                builder.ConfigureServices(services =>
                    services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing.AddInMemoryExporter(activities)));
            });
            return new WebApplicationFactoryScope(factory);
        }

        private sealed class WebApplicationFactoryScope : IDisposable
        {
            public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory { get; }
            public HttpClient Client { get; }

            public WebApplicationFactoryScope(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
            {
                Factory = factory;
                Client = factory.CreateClient();
            }

            public void Dispose()
            {
                Client.Dispose();
                Factory.Dispose();
            }
        }

        private static async Task AuthenticateAsync(HttpClient client)
        {
            const string email = "telemetry-buyer@example.test";
            await client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password = "User123!" });
            var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "User123!" });
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        }

        private static Variant SeedVariant(IServiceProvider services, int quantity, string sku)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id };
            context.Products.Add(product);
            context.SaveChanges();

            var variant = new Variant { Name = "Small", Sku = sku, Quantity = quantity, Active = true, ProductId = product.Id };
            context.Variants.Add(variant);
            context.SaveChanges();
            return variant;
        }

        private static async Task<int> BuyAsync(HttpClient client, int variantId, int quantity)
        {
            var response = await client.PostAsJsonAsync("/api/orders", new { VariantId = variantId, Quantity = quantity });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<OrderResponse>())!.Id;
        }

        private static async Task WaitForResolvedAsync(HttpClient client, int orderId)
        {
            await WaitUntilAsync(async () =>
            {
                var order = await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{orderId}");
                return order!.Status != OrderStatus.Pending;
            }, $"order {orderId} to be resolved");
        }

        private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                {
                    return;
                }

                await Task.Delay(200);
            }

            throw new TimeoutException($"Timed out waiting for {what}.");
        }

        [Test]
        public async Task Purchase_RequestPublishAndConsumerSpans_ShareOneTrace()
        {
            var activities = new ThreadSafeActivities();
            using var app = StartApp(activities);
            var variant = SeedVariant(app.Factory.Services, quantity: 10, sku: "TRACE-1");
            await AuthenticateAsync(app.Client);

            var orderId = await BuyAsync(app.Client, variant.Id, 1);
            await WaitForResolvedAsync(app.Client, orderId);
            await WaitUntilAsync(
                () => Task.FromResult(activities.Any(a => a.DisplayName == "order-placed process")),
                "the consumer span to be exported");

            var request = activities.Single(a => a.DisplayName == "POST api/Orders");
            var publish = activities.Single(a => a.DisplayName == "order-placed publish");
            var process = activities.Single(a => a.DisplayName == "order-placed process");
            var stockCheck = activities.Single(a => a.DisplayName == "order.stock-check");

            Assert.That(publish.TraceId, Is.EqualTo(request.TraceId), "publish is part of the buy request's trace");
            Assert.That(process.TraceId, Is.EqualTo(request.TraceId), "consumer processing is part of the same trace");
            Assert.That(process.ParentSpanId, Is.EqualTo(publish.SpanId), "the consumer span is a child of the publish span");
            Assert.That(stockCheck.TraceId, Is.EqualTo(request.TraceId));
            Assert.That(process.Kind, Is.EqualTo(ActivityKind.Consumer));
            Assert.That(stockCheck.GetTagItem("order.id"), Is.EqualTo(orderId));
            Assert.That(stockCheck.GetTagItem("variant.sku"), Is.EqualTo("TRACE-1"));
            Assert.That(stockCheck.GetTagItem("order.outcome"), Is.EqualTo("confirmed"));
        }

        [Test]
        public async Task HealthChecks_AreNotTraced()
        {
            var activities = new ThreadSafeActivities();
            using var app = StartApp(activities);

            await app.Client.GetAsync("/health/live");
            await app.Client.GetAsync("/version");

            // A span is exported when it stops, which can be just after the response is sent.
            await WaitUntilAsync(
                () => Task.FromResult(activities.Any(a => a.GetTagItem("url.path")?.ToString() == "/version")),
                "the /version span to be exported");

            Assert.That(activities.Any(a => a.DisplayName.Contains("health", StringComparison.OrdinalIgnoreCase)), Is.False,
                "health checks are not traced");
        }

        [Test]
        public async Task SamplingRatioZero_ExportsNoTraces()
        {
            var activities = new ThreadSafeActivities();
            using var app = StartApp(activities, samplingRatio: "0");

            await app.Client.GetAsync("/version");
            await Task.Delay(500);

            Assert.That(activities, Is.Empty);
        }

        [Test]
        public async Task CancelledOrder_RecordsCancelledOutcomeAndLeavesThePendingCount()
        {
            var processed = new ConcurrentQueue<(string? Outcome, string? Reason)>();
            var durations = new ConcurrentQueue<string?>();
            var pending = new ConcurrentQueue<long>();

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Ecommerce_Backend")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (instrument.Name == OrderMetrics.OrdersProcessedName)
                {
                    string? outcome = null, reason = null;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "outcome") outcome = tag.Value?.ToString();
                        if (tag.Key == "reason") reason = tag.Value?.ToString();
                    }
                    processed.Enqueue((outcome, reason));
                }
                else if (instrument.Name == OrderMetrics.OrdersPendingName)
                {
                    pending.Enqueue(value);
                }
            });
            listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            {
                if (instrument.Name == OrderMetrics.ProcessingDurationName)
                {
                    durations.Enqueue(tags.ToArray().Single(t => t.Key == "outcome").Value?.ToString());
                }
            });
            listener.Start();

            using var app = StartApp(new ThreadSafeActivities());
            var variant = SeedVariant(app.Factory.Services, quantity: 5, sku: "CANCEL-1");
            await AuthenticateAsync(app.Client);

            // Inserted directly so the consumer never sees it.
            int orderId;
            using (var scope = app.Factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var order = new Order
                {
                    ApplicationUserId = context.Users.Single(u => u.Email == "telemetry-buyer@example.test").Id,
                    VariantId = variant.Id,
                    Quantity = 1,
                    UnitPrice = 100,
                    OrderDate = DateTime.UtcNow,
                    Status = OrderStatus.Pending
                };
                context.Orders.Add(order);
                context.SaveChanges();
                orderId = order.Id;
            }

            listener.RecordObservableInstruments();
            Assert.That(pending.Last(), Is.EqualTo(1), "the order counts as Pending before it is cancelled");

            var response = await app.Client.PostAsync($"/api/orders/{orderId}/cancel", null);
            response.EnsureSuccessStatusCode();

            listener.RecordObservableInstruments();
            Assert.That(pending.Last(), Is.EqualTo(0), "a cancelled order no longer counts as Pending");
            Assert.That(processed.ToArray(), Is.EquivalentTo(new (string?, string?)[] { ("cancelled", null) }));
            Assert.That(durations.ToArray(), Is.EquivalentTo(new[] { "cancelled" }));
        }

        [Test]
        public async Task ProcessedOrders_RecordOneConfirmedAndOneRejectedWithTheReason()
        {
            var processed = new ConcurrentQueue<(string? Outcome, string? Reason)>();
            var placed = 0L;
            var durations = new ConcurrentQueue<string?>();

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Ecommerce_Backend")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (instrument.Name == OrderMetrics.OrdersProcessedName)
                {
                    string? outcome = null, reason = null;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "outcome") outcome = tag.Value?.ToString();
                        if (tag.Key == "reason") reason = tag.Value?.ToString();
                    }
                    processed.Enqueue((outcome, reason));
                }
                else if (instrument.Name == OrderMetrics.OrdersPlacedName)
                {
                    Interlocked.Add(ref placed, value);
                }
            });
            listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            {
                if (instrument.Name == OrderMetrics.ProcessingDurationName)
                {
                    durations.Enqueue(tags.ToArray().Single(t => t.Key == "outcome").Value?.ToString());
                }
            });
            listener.Start();

            using var app = StartApp(new ThreadSafeActivities());
            var variant = SeedVariant(app.Factory.Services, quantity: 1, sku: "METRIC-1");
            await AuthenticateAsync(app.Client);

            var first = await BuyAsync(app.Client, variant.Id, 1);
            await WaitForResolvedAsync(app.Client, first);
            var second = await BuyAsync(app.Client, variant.Id, 1);
            await WaitForResolvedAsync(app.Client, second);

            Assert.That(processed.ToArray(), Is.EquivalentTo(new (string?, string?)[]
            {
                ("confirmed", null),
                ("rejected", "insufficient_stock")
            }));
            Assert.That(Interlocked.Read(ref placed), Is.EqualTo(2));
            Assert.That(durations.ToArray(), Is.EquivalentTo(new[] { "confirmed", "rejected" }));
        }

        [TestCase("Telemetry:SamplingRatio", "1.5", "Telemetry:SamplingRatio")]
        [TestCase("Telemetry:MetricExportIntervalSeconds", "0", "Telemetry:MetricExportIntervalSeconds")]
        public void InvalidTelemetryConfiguration_FailsAtStartup(string key, string value, string expectedMessageFragment)
        {
            using var factory = new CustomWebApplicationFactory(enableOrderProcessing: false)
                .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));

            var exception = Assert.Catch(() => factory.CreateClient());

            Assert.That(exception!.ToString(), Does.Contain(expectedMessageFragment));
        }

        [Test]
        public void OtlpExporterWithoutAnEndpoint_FailsAtStartup()
        {
            using var factory = new CustomWebApplicationFactory(enableOrderProcessing: false)
                .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { ["Telemetry:Exporter"] = "Otlp" })));

            var exception = Assert.Catch(() => factory.CreateClient());

            Assert.That(exception!.ToString(), Does.Contain("Telemetry:OtlpEndpoint"));
        }
    }
}
