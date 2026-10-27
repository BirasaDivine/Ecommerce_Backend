using Ecommerce_Backend.Data;
using Ecommerce_Backend.Messaging;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Ecommerce_Backend.Tests.Controllers
{
    [TestFixture]
    public class OrdersControllerTests
    {
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

        private CustomWebApplicationFactory _factory;
        private HttpClient _client;

        [SetUp]
        public void Setup()
        {
            _factory = new CustomWebApplicationFactory();
            _client = _factory.CreateClient();
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
            _factory?.Dispose();
        }

        private record LoginResponse(string Token);
        private record OrderResponse(int Id, int VariantId, string Sku, int Quantity, decimal UnitPrice, decimal TotalPrice, OrderStatus Status, string? RejectionReason);

        private Task<string> RegisterAndLoginAsync(string email) => RegisterAndLoginAsync(_client, email);

        private static async Task<string> RegisterAndLoginAsync(HttpClient client, string email)
        {
            await client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password = "User123!" });
            var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "User123!" });
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return result!.Token;
        }
        private async Task<string> LoginAsSeededAdminAsync(HttpClient client)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@ecommerce.local", Password = "Admin123!" });
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return result!.Token;
        }

        private static Variant SeedVariant(CustomWebApplicationFactory factory, int quantity = 10, bool active = true, decimal? overridePrice = null, string sku = "CT-S")
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id };
            context.Products.Add(product);
            context.SaveChanges();

            var variant = new Variant
            {
                Name = "Small",
                Sku = sku,
                Quantity = quantity,
                Active = active,
                Price = overridePrice,
                ProductId = product.Id
            };
            context.Variants.Add(variant);
            context.SaveChanges();

            return variant;
        }

        private Variant SeedVariant(int quantity = 10, bool active = true, decimal? overridePrice = null, string sku = "CT-S")
        {
            return SeedVariant(_factory, quantity, active, overridePrice, sku);
        }

        private static async Task<OrderResponse> PollUntilResolvedAsync(HttpClient client, int orderId, TimeSpan? timeout = null)
        {
            var effectiveTimeout = timeout ?? PollTimeout;
            var deadline = DateTime.UtcNow + effectiveTimeout;
            while (DateTime.UtcNow < deadline)
            {
                var response = await client.GetAsync($"/api/orders/{orderId}");
                response.EnsureSuccessStatusCode();
                var order = await response.Content.ReadFromJsonAsync<OrderResponse>();

                if (order!.Status != OrderStatus.Pending)
                {
                    return order;
                }

                await Task.Delay(PollInterval);
            }

            throw new TimeoutException($"Order {orderId} did not resolve within {effectiveTimeout}.");
        }

        // Inserted directly, without publishing a message, so the consumer cannot resolve it behind the test's back.
        private static Order SeedOrder(CustomWebApplicationFactory factory, string buyerEmail, int variantId, OrderStatus status, int quantity = 1)
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var order = new Order
            {
                ApplicationUserId = context.Users.Single(u => u.Email == buyerEmail).Id,
                VariantId = variantId,
                Quantity = quantity,
                UnitPrice = 100,
                OrderDate = DateTime.UtcNow,
                Status = status
            };
            context.Orders.Add(order);
            context.SaveChanges();
            return order;
        }

        private static Order GetOrder(CustomWebApplicationFactory factory, int orderId)
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return context.Orders.AsNoTracking().First(o => o.Id == orderId);
        }

        private Task<HttpResponseMessage> CancelAsync(int orderId) => _client.PostAsync($"/api/orders/{orderId}/cancel", null);

        private static Task PublishOrderPlacedAsync(CustomWebApplicationFactory factory, Order order, string sku)
        {
            return factory.Services.GetRequiredService<IOrderEventPublisher>().PublishOrderPlacedAsync(new OrderPlacedMessage
            {
                OrderId = order.Id,
                VariantId = order.VariantId,
                Sku = sku,
                Quantity = order.Quantity
            });
        }

        private static Variant GetVariant(CustomWebApplicationFactory factory, int variantId)
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return context.Variants.First(v => v.Id == variantId);
        }

        [Test]
        public async Task Buy_WithoutAuth_Returns401()
        {
            var variant = SeedVariant();

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 1 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task Buy_AsAuthenticatedUser_AcceptsThenConfirmsAndReducesStock()
        {
            var variant = SeedVariant(quantity: 10);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 3 });
            var accepted = await response.Content.ReadFromJsonAsync<OrderResponse>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(accepted!.Status, Is.EqualTo(OrderStatus.Pending));

            var resolved = await PollUntilResolvedAsync(_client, accepted.Id);

            Assert.That(resolved.Status, Is.EqualTo(OrderStatus.Confirmed));
            Assert.That(resolved.Quantity, Is.EqualTo(3));
            Assert.That(resolved.TotalPrice, Is.EqualTo(300));

            var updatedVariant = GetVariant(_factory, variant.Id);
            Assert.That(updatedVariant.Quantity, Is.EqualTo(7));
        }

        [Test]
        public async Task Buy_MoreThanAvailableStock_ResolvesToRejectedAndDoesNotChangeStock()
        {
            var variant = SeedVariant(quantity: 2);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 5 });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            var accepted = await response.Content.ReadFromJsonAsync<OrderResponse>();

            var resolved = await PollUntilResolvedAsync(_client, accepted!.Id);

            Assert.That(resolved.Status, Is.EqualTo(OrderStatus.Rejected));
            Assert.That(resolved.RejectionReason, Is.Not.Null.And.Not.Empty);

            var updatedVariant = GetVariant(_factory, variant.Id);
            Assert.That(updatedVariant.Quantity, Is.EqualTo(2));
        }

        [Test]
        public async Task Buy_InactiveVariant_Returns400()
        {
            var variant = SeedVariant(quantity: 10, active: false);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 1 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task Buy_NonExistentVariant_Returns404()
        {
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = 9999, Quantity = 1 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task Buy_UsesVariantPriceOverrideWhenSet()
        {
            var variant = SeedVariant(quantity: 10, overridePrice: 75);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 2 });
            var accepted = await response.Content.ReadFromJsonAsync<OrderResponse>();

            var resolved = await PollUntilResolvedAsync(_client, accepted!.Id);

            Assert.That(resolved.UnitPrice, Is.EqualTo(75));
            Assert.That(resolved.TotalPrice, Is.EqualTo(150));
        }

        [Test]
        public async Task GetOrderById_AsDifferentUser_Returns403()
        {
            var variant = SeedVariant(quantity: 10);
            var buyerToken = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", buyerToken);

            var buyResponse = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 1 });
            var order = await buyResponse.Content.ReadFromJsonAsync<OrderResponse>();

            var otherToken = await RegisterAndLoginAsync("other@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

            var response = await _client.GetAsync($"/api/orders/{order!.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task Buy_ConcurrentRequestsExceedingStock_ConfirmsExactlyAvailableQuantity()
        {
            const int initialStock = 6;
            const int concurrentBuyers = 20;

            var variant = SeedVariant(quantity: initialStock, sku: "CT-LOAD");
            var tokens = await Task.WhenAll(Enumerable.Range(0, concurrentBuyers)
                .Select(i => RegisterAndLoginAsync($"loadbuyer{i}@ecommerce.local")));

            var orderIds = new List<int>();
            foreach (var token in tokens)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
                {
                    Content = JsonContent.Create(new { VariantId = variant.Id, Quantity = 1 })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var client = _factory.CreateClient();
                var response = await client.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
                var accepted = await response.Content.ReadFromJsonAsync<OrderResponse>();
                orderIds.Add(accepted!.Id);
            }

            // Poll as Admin, since each order belongs to a different buyer and
            // GetOrderById only allows the owner or an Admin to view it.
            using var adminClient = _factory.CreateClient();
            var adminToken = await LoginAsSeededAdminAsync(adminClient);
            adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var resolved = await Task.WhenAll(orderIds.Select(id => PollUntilResolvedAsync(adminClient, id)));

            var confirmedCount = resolved.Count(o => o.Status == OrderStatus.Confirmed);
            var rejectedCount = resolved.Count(o => o.Status == OrderStatus.Rejected);

            Assert.That(confirmedCount, Is.EqualTo(initialStock));
            Assert.That(rejectedCount, Is.EqualTo(concurrentBuyers - initialStock));

            var updatedVariant = GetVariant(_factory, variant.Id);
            Assert.That(updatedVariant.Quantity, Is.EqualTo(0));
        }

        [Test]
        public async Task Cancel_OwnPendingOrder_Returns200AndOrderBecomesCancelled()
        {
            var variant = SeedVariant(quantity: 10);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var order = SeedOrder(_factory, "buyer@ecommerce.local", variant.Id, OrderStatus.Pending);

            var response = await CancelAsync(order.Id);
            var cancelled = await response.Content.ReadFromJsonAsync<OrderResponse>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(cancelled!.Status, Is.EqualTo(OrderStatus.Cancelled));

            var fetched = await _client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}");
            Assert.That(fetched!.Status, Is.EqualTo(OrderStatus.Cancelled));
            Assert.That(GetVariant(_factory, variant.Id).Quantity, Is.EqualTo(10));
        }

        [Test]
        public async Task Cancel_WithoutAuth_Returns401()
        {
            var response = await CancelAsync(1);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task Cancel_UnknownOrder_Returns404()
        {
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await CancelAsync(9999);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task Cancel_AnotherUsersOrder_Returns404AndLeavesOrderPending()
        {
            var variant = SeedVariant(quantity: 10);
            await RegisterAndLoginAsync("buyer@ecommerce.local");
            var otherToken = await RegisterAndLoginAsync("other@ecommerce.local");
            var order = SeedOrder(_factory, "buyer@ecommerce.local", variant.Id, OrderStatus.Pending);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

            var response = await CancelAsync(order.Id);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(GetOrder(_factory, order.Id).Status, Is.EqualTo(OrderStatus.Pending));
        }

        [TestCase(OrderStatus.Confirmed)]
        [TestCase(OrderStatus.Rejected)]
        [TestCase(OrderStatus.Cancelled)]
        public async Task Cancel_OrderThatIsNoLongerPending_Returns409AndKeepsItsStatus(OrderStatus status)
        {
            var variant = SeedVariant(quantity: 10);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var order = SeedOrder(_factory, "buyer@ecommerce.local", variant.Id, status);

            var response = await CancelAsync(order.Id);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(GetOrder(_factory, order.Id).Status, Is.EqualTo(status));
        }

        [Test]
        public async Task Consumer_CancelledOrderMessage_IsSkippedAndStockIsUntouched()
        {
            const string sku = "CT-CANCELLED";
            var variant = SeedVariant(quantity: 10, sku: sku);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cancelled = SeedOrder(_factory, "buyer@ecommerce.local", variant.Id, OrderStatus.Cancelled, quantity: 4);
            await PublishOrderPlacedAsync(_factory, cancelled, sku);

            // Messages of one session (SKU) are handled in order, so once this later order is resolved the
            // cancelled order's message has been handled too.
            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 1 });
            var later = await response.Content.ReadFromJsonAsync<OrderResponse>();
            var resolved = await PollUntilResolvedAsync(_client, later!.Id);

            Assert.That(resolved.Status, Is.EqualTo(OrderStatus.Confirmed));
            Assert.That(GetOrder(_factory, cancelled.Id).Status, Is.EqualTo(OrderStatus.Cancelled));
            Assert.That(GetVariant(_factory, variant.Id).Quantity, Is.EqualTo(9), "only the later order took stock");
        }

        [Test]
        public async Task CancelRacingTheConsumer_ExactlyOneWinsAndStockMatches()
        {
            const string sku = "CT-RACE";
            const int initialStock = 100;
            const int rounds = 20;

            var variant = SeedVariant(quantity: initialStock, sku: sku);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var outcomes = new List<(int OrderId, OrderStatus Status)>();
            for (var i = 0; i < rounds; i++)
            {
                var order = SeedOrder(_factory, "buyer@ecommerce.local", variant.Id, OrderStatus.Pending);

                var publish = PublishOrderPlacedAsync(_factory, order, sku);
                var cancel = CancelAsync(order.Id);
                await Task.WhenAll(publish, cancel);

                var response = await cancel;
                var current = GetOrder(_factory, order.Id);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Assert.That(current.Status, Is.EqualTo(OrderStatus.Cancelled), $"round {i}: cancel won");
                }
                else
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), $"round {i}");
                    Assert.That(current.Status, Is.EqualTo(OrderStatus.Confirmed), $"round {i}: consumer won");
                }

                outcomes.Add((order.Id, current.Status));
            }

            // A real order for the same SKU: once it is resolved every earlier message has been handled.
            var sentinelResponse = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 1 });
            var sentinel = await sentinelResponse.Content.ReadFromJsonAsync<OrderResponse>();
            await PollUntilResolvedAsync(_client, sentinel!.Id);

            foreach (var (orderId, status) in outcomes)
            {
                Assert.That(GetOrder(_factory, orderId).Status, Is.EqualTo(status), $"order {orderId} kept its outcome");
            }

            var confirmed = outcomes.Count(o => o.Status == OrderStatus.Confirmed);
            Assert.That(GetVariant(_factory, variant.Id).Quantity, Is.EqualTo(initialStock - confirmed - 1),
                "stock dropped only for Confirmed orders (and the sentinel), never for a Cancelled one");
        }

        [Test]
        public async Task ConsumerRecovery_PendingOrdersResolveExactlyOnceAfterProcessorRestart()
        {
            _client.Dispose();
            _factory.Dispose();

            var databaseName = "TestDb_" + Guid.NewGuid();
            using var firstFactory = new CustomWebApplicationFactory(databaseName, enableOrderProcessing: false);
            using var firstClient = firstFactory.CreateClient();

            var variant = SeedVariant(firstFactory, quantity: 5, sku: "CT-RECOVERY-" + Guid.NewGuid());
            var token = await RegisterAndLoginAsync(firstClient, "recoverybuyer@ecommerce.local");
            firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var orderIds = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                var response = await firstClient.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 1 });
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
                var accepted = await response.Content.ReadFromJsonAsync<OrderResponse>();
                Assert.That(accepted!.Status, Is.EqualTo(OrderStatus.Pending));
                orderIds.Add(accepted.Id);
            }

            using var secondFactory = new CustomWebApplicationFactory(databaseName);
            using var secondClient = secondFactory.CreateClient();
            secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resolved = await Task.WhenAll(orderIds.Select(id => PollUntilResolvedAsync(secondClient, id)));

            Assert.That(resolved, Has.All.Matches<OrderResponse>(o => o.Status == OrderStatus.Confirmed));

            var updatedVariant = GetVariant(secondFactory, variant.Id);
            Assert.That(updatedVariant.Quantity, Is.EqualTo(2));
        }
    }
}
