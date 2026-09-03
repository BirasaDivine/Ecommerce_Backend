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

        private (Category category, Product product) SeedProduct(string productName = "Cotton Top", decimal basePrice = 100)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Category_" + Guid.NewGuid() };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product { Name = productName, BasePrice = basePrice, CategoryId = category.Id };
            context.Products.Add(product);
            context.SaveChanges();

            return (category, product);
        }

        private Variant SeedVariant(string sku = "CT-S", int quantity = 10)
        {
            var (_, product) = SeedProduct();

            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var variant = new Variant { Name = "Small", Sku = sku, Quantity = quantity, ProductId = product.Id };
            context.Variants.Add(variant);
            context.SaveChanges();

            return variant;
        }

        [Test]
        public async Task GetAllVariants_WithoutAuth_Returns401()
        {
            var response = await _client.GetAsync("/api/variants");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task GetAllVariants_AsNonAdmin_Returns403()
        {
            var token = await GetUserTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.GetAsync("/api/variants");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task GetAllVariants_AsAdmin_ReturnsAllSeededVariants()
        {
            var (_, product) = SeedProduct();

            using (var scope = _factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Variants.AddRange(
                    new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id },
                    new Variant { Name = "Medium", Sku = "TOP-M", Quantity = 5, ProductId = product.Id });
                context.SaveChanges();
            }

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.GetAsync("/api/variants");
            var variants = await response.Content.ReadFromJsonAsync<List<VariantResponse>>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(variants, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task GetAllVariants_WithProductIdFilter_ReturnsOnlyMatchingVariants()
        {
            var (_, productA) = SeedProduct("Cotton Top");
            var (_, productB) = SeedProduct("Denim Jeans");

            using (var scope = _factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Variants.AddRange(
                    new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = productA.Id },
                    new Variant { Name = "32", Sku = "JEANS-32", Quantity = 5, ProductId = productB.Id });
                context.SaveChanges();
            }

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.GetAsync($"/api/variants?productId={productA.Id}");
            var variants = await response.Content.ReadFromJsonAsync<List<VariantResponse>>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(variants, Has.Count.EqualTo(1));
            Assert.That(variants![0].Sku, Is.EqualTo("TOP-S"));
        }

        [Test]
        public async Task GetVariantById_WithoutAuth_Returns401()
        {
            var response = await _client.GetAsync("/api/variants/9999");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task GetVariantById_AsAdmin_WithNonExistingId_Returns404()
        {
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.GetAsync("/api/variants/9999");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task GetVariantById_AsAdmin_WithExistingId_Returns200()
        {
            var (_, product) = SeedProduct();

            Variant variant;
            using (var scope = _factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                variant = new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
                context.Variants.Add(variant);
                context.SaveChanges();
            }

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.GetAsync($"/api/variants/{variant.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task CreateVariant_WithoutAuth_Returns401()
        {
            var (_, product) = SeedProduct();

            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task CreateVariant_AsNonAdmin_Returns403()
        {
            var (_, product) = SeedProduct();
            var token = await GetUserTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task CreateVariant_AsAdmin_WithValidData_Returns201()
        {
            var (_, product) = SeedProduct();
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        [Test]
        public async Task CreateVariant_AsAdmin_WithNonExistingProduct_Returns400()
        {
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = 9999 };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateVariant_AsAdmin_WithDuplicateSku_Returns400()
        {
            var (_, product) = SeedProduct();
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var first = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            await _client.PostAsJsonAsync("/api/variants", first);

            var duplicate = new { Name = "Large", Sku = "TOP-S", Quantity = 2, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", duplicate);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateVariant_AsAdmin_WithNegativeQuantity_Returns400()
        {
            var (_, product) = SeedProduct();
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = -1, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
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

        private class VariantResponse
        {
            public int Id { get; set; }
            public string Sku { get; set; } = string.Empty;
        }
    }
}
