using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
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

        [Test]
        public async Task GetAllVariants_ReturnsAllSeededVariants()
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

            var response = await _client.GetAsync($"/api/variants?productId={productA.Id}");
            var variants = await response.Content.ReadFromJsonAsync<List<VariantResponse>>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(variants, Has.Count.EqualTo(1));
            Assert.That(variants![0].Sku, Is.EqualTo("TOP-S"));
        }

        [Test]
        public async Task GetVariantById_WithNonExistingId_Returns404()
        {
            var response = await _client.GetAsync("/api/variants/9999");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task GetVariantById_WithExistingId_Returns200()
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

            var response = await _client.GetAsync($"/api/variants/{variant.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task CreateVariant_WithValidData_Returns201()
        {
            var (_, product) = SeedProduct();

            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        [Test]
        public async Task CreateVariant_WithNonExistingProduct_Returns400()
        {
            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = 9999 };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateVariant_WithDuplicateSku_Returns400()
        {
            var (_, product) = SeedProduct();

            var first = new { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            await _client.PostAsJsonAsync("/api/variants", first);

            var duplicate = new { Name = "Large", Sku = "TOP-S", Quantity = 2, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", duplicate);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateVariant_WithNegativeQuantity_Returns400()
        {
            var (_, product) = SeedProduct();

            var payload = new { Name = "Small", Sku = "TOP-S", Quantity = -1, ProductId = product.Id };
            var response = await _client.PostAsJsonAsync("/api/variants", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        private class VariantResponse
        {
            public int Id { get; set; }
            public string Sku { get; set; } = string.Empty;
        }
    }
}
