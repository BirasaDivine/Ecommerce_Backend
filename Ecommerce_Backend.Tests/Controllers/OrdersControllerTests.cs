using Ecommerce_Backend.Data;
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

        // Matches the default SeedAdmin:Email / SeedAdmin:Password seeded on startup
        // in Development (Program.cs) - no registration needed, the account already exists.
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
        public async Task ConsumerRecovery_PendingOrdersResolveExactlyOnceAfterProcessorRestart()
        {
            // All Service Bus sessions live on one shared emulator queue, so the base
            // fixture's own _factory (from Setup, still alive here) would otherwise run
            // a second live consumer that competes with this test's factories for the
            // same session. Retire it first so only this test's own hosts are listening.
            _client.Dispose();
            _factory.Dispose();

            var databaseName = "TestDb_" + Guid.NewGuid();

            // The first host never starts the consumer at all, so it can never touch
            // the queue - orders are placed and published but nothing drains them,
            // simulating the app being down between placement and processing.
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
