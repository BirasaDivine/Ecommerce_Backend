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
    public class VariantsControllerTests
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

        private async Task<string> GetAdminTokenAsync()
        {
            var loginPayload = new { Email = "admin@ecommerce.local", Password = "Admin123!" };
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return result!.Token;
        }

        private async Task<string> GetUserTokenAsync()
        {
            var email = $"user_{Guid.NewGuid()}@ecommerce.local";
            await _client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password = "User123!" });

            var response = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "User123!" });
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return result!.Token;
        }

        private Variant SeedVariant(string sku = "CT-S", int quantity = 10)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id };
            context.Products.Add(product);
            context.SaveChanges();

            var variant = new Variant { Name = "Small", Sku = sku, Quantity = quantity, ProductId = product.Id };
            context.Variants.Add(variant);
            context.SaveChanges();

            return variant;
        }

        [Test]
        public async Task UpdateStock_AsAdmin_UpdatesQuantity()
        {
            var variant = SeedVariant();
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PatchAsJsonAsync($"/api/variants/{variant.Sku}/stock", new { Quantity = 25 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updated = context.Variants.First(v => v.Sku == variant.Sku);
            Assert.That(updated.Quantity, Is.EqualTo(25));
        }

        [Test]
        public async Task UpdateStock_WithoutAuth_Returns401()
        {
            var variant = SeedVariant();

            var response = await _client.PatchAsJsonAsync($"/api/variants/{variant.Sku}/stock", new { Quantity = 25 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task UpdateStock_AsNonAdmin_Returns403()
        {
            var variant = SeedVariant();
            var token = await GetUserTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PatchAsJsonAsync($"/api/variants/{variant.Sku}/stock", new { Quantity = 25 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task UpdateStock_NonExistentSku_Returns404()
        {
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PatchAsJsonAsync("/api/variants/DOES-NOT-EXIST/stock", new { Quantity = 25 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task UpdateStock_NegativeQuantity_Returns400()
        {
            var variant = SeedVariant();
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PatchAsJsonAsync($"/api/variants/{variant.Sku}/stock", new { Quantity = -5 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }
    }
}
