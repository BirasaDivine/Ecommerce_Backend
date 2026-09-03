using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Tests;
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
        private record OrderResponse(int Id, int VariantId, string Sku, int Quantity, decimal UnitPrice, decimal TotalPrice);

        private async Task<string> RegisterAndLoginAsync(string email)
        {
            await _client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password = "User123!" });
            var response = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "User123!" });
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return result!.Token;
        }

        private Variant SeedVariant(int quantity = 10, bool active = true, decimal? overridePrice = null)
        {
            using var scope = _factory.Services.CreateScope();
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
                Sku = "CT-S",
                Quantity = quantity,
                Active = active,
                Price = overridePrice,
                ProductId = product.Id
            };
            context.Variants.Add(variant);
            context.SaveChanges();

            return variant;
        }

        [Test]
        public async Task Buy_WithoutAuth_Returns401()
        {
            var variant = SeedVariant();

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 1 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task Buy_AsAuthenticatedUser_CreatesOrderAndReducesStock()
        {
            var variant = SeedVariant(quantity: 10);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 3 });
            var order = await response.Content.ReadFromJsonAsync<OrderResponse>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(order!.Quantity, Is.EqualTo(3));
            Assert.That(order.TotalPrice, Is.EqualTo(300));

            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedVariant = context.Variants.First(v => v.Id == variant.Id);
            Assert.That(updatedVariant.Quantity, Is.EqualTo(7));
        }

        [Test]
        public async Task Buy_MoreThanAvailableStock_Returns400AndDoesNotChangeStock()
        {
            var variant = SeedVariant(quantity: 2);
            var token = await RegisterAndLoginAsync("buyer@ecommerce.local");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/orders", new { VariantId = variant.Id, Quantity = 5 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedVariant = context.Variants.First(v => v.Id == variant.Id);
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
            var order = await response.Content.ReadFromJsonAsync<OrderResponse>();

            Assert.That(order!.UnitPrice, Is.EqualTo(75));
            Assert.That(order.TotalPrice, Is.EqualTo(150));
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
    }
}
