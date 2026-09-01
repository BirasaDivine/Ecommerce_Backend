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
    public class ProductsControllerTests
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
        private async Task<string> GetAdminTokenAsync()
        {
            var loginPayload = new { Email = "admin@ecommerce.local", Password = "Admin123!" };
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return result!.Token;
        }

        private record LoginResponse(string Token);

        [Test]
        public async Task GetProductById_WithNonExistingId_Returns404()
        {
            var response = await _client.GetAsync("/api/products/9999");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
        [Test]
        public async Task GetProductById_WithExistingId_Returns200()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id };
            context.Products.Add(product);
            context.SaveChanges();

            var response = await _client.GetAsync($"/api/products/{product.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task GetProductById_ReturnsCategoryNameAndActiveVariantsOnly()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product
            {
                Name = "Cotton Top",
                BasePrice = 100,
                CategoryId = category.Id,
                Variants = new List<Variant>
                {
                    new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, Active = true },
                    new Variant { Name = "Discontinued", Sku = "TOP-D", Quantity = 0, Active = false },
                }
            };
            context.Products.Add(product);
            context.SaveChanges();

            var response = await _client.GetAsync($"/api/products/{product.Id}");
            var dto = await response.Content.ReadFromJsonAsync<ProductResponse>();

            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.CategoryName, Is.EqualTo("Topwear"));
            Assert.That(dto.Variants, Has.Count.EqualTo(1));
            Assert.That(dto.Variants[0].Name, Is.EqualTo("Small"));
        }

        [Test]
        public async Task CreateProduct_WithValidData_Returns201()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Name = "Bottomwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Jeans", BasePrice = 50, CategoryId = category.Id };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        [Test]
        public async Task CreateProduct_WithNegativePrice_Returns400()
        {
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Bad Product", BasePrice = -10, CategoryId = 1 };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateProduct_WithMissingName_Returns400()
        {
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { BasePrice = 50, CategoryId = 1 };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateProduct_WithNonExistentCategory_Returns400()
        {
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Orphan Product", BasePrice = 20, CategoryId = 9999 };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateProduct_WithNonTerminalCategory_Returns400()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parent = new Category { Name = "Women" };
            context.Categories.Add(parent);
            context.SaveChanges();

            var child = new Category { Name = "Dresses", ParentCategoryId = parent.Id };
            context.Categories.Add(child);
            context.SaveChanges();

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Trench Coat", BasePrice = 90, CategoryId = parent.Id };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateProduct_WithTerminalCategory_Returns201()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parent = new Category { Name = "Women" };
            context.Categories.Add(parent);
            context.SaveChanges();

            var child = new Category { Name = "Dresses", ParentCategoryId = parent.Id };
            context.Categories.Add(child);
            context.SaveChanges();

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { Name = "Summer Dress", BasePrice = 45, CategoryId = child.Id };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        [Test]
        public async Task CreateProduct_WithVariants_PersistsVariants()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Name = "Bottomwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                Name = "Jeans",
                BasePrice = 50,
                CategoryId = category.Id,
                Variants = new[]
                {
                    new { Name = "32", Sku = "JEANS-32", Quantity = 10 },
                    new { Name = "34", Sku = "JEANS-34", Quantity = 5 },
                }
            };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(context.Variants.Count(v => v.Sku == "JEANS-32" || v.Sku == "JEANS-34"), Is.EqualTo(2));
        }

        [Test]
        public async Task CreateProduct_WithDuplicateVariantSku_Returns400()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Name = "Bottomwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var existingProduct = new Product
            {
                Name = "Existing Jeans",
                BasePrice = 40,
                CategoryId = category.Id,
                Variants = new List<Variant> { new Variant { Name = "32", Sku = "JEANS-32", Quantity = 3 } }
            };
            context.Products.Add(existingProduct);
            context.SaveChanges();

            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                Name = "New Jeans",
                BasePrice = 50,
                CategoryId = category.Id,
                Variants = new[] { new { Name = "32", Sku = "JEANS-32", Quantity = 10 } }
            };
            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        private record ProductListItem(int Id, string Name, decimal BasePrice);

        private void SeedProducts(AppDbContext context, Category category)
        {
            context.Products.AddRange(
                new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id },
                new Product { Name = "Denim Jacket", BasePrice = 150, CategoryId = category.Id },
                new Product { Name = "Cotton Scarf", BasePrice = 20, CategoryId = category.Id }
            );
            context.SaveChanges();
        }

        [Test]
        public async Task GetAllProducts_WithNoFilters_ReturnsAllProducts()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Name = "Apparel" };
            context.Categories.Add(category);
            context.SaveChanges();
            SeedProducts(context, category);

            var response = await _client.GetAsync("/api/products");
            var products = await response.Content.ReadFromJsonAsync<List<ProductListItem>>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(products, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task GetAllProducts_FilteredByName_ReturnsMatchingProducts()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Name = "Apparel" };
            context.Categories.Add(category);
            context.SaveChanges();
            SeedProducts(context, category);

            var response = await _client.GetAsync("/api/products?name=cotton");
            var products = await response.Content.ReadFromJsonAsync<List<ProductListItem>>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(products, Has.Count.EqualTo(2));
            Assert.That(products!.Select(p => p.Name), Is.EquivalentTo(new[] { "Cotton Top", "Cotton Scarf" }));
        }

        [Test]
        public async Task GetAllProducts_FilteredByMaxPrice_ReturnsMatchingProducts()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Name = "Apparel" };
            context.Categories.Add(category);
            context.SaveChanges();
            SeedProducts(context, category);

            var response = await _client.GetAsync("/api/products?maxPrice=100");
            var products = await response.Content.ReadFromJsonAsync<List<ProductListItem>>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(products, Has.Count.EqualTo(2));
            Assert.That(products!.Select(p => p.Name), Is.EquivalentTo(new[] { "Cotton Top", "Cotton Scarf" }));
        }

        [Test]
        public async Task GetAllProducts_ReturnsEagerLoadedCategoryAndVariants()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category { Name = "Apparel" };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id };
            context.Products.Add(product);
            context.SaveChanges();

            context.Variants.Add(new Variant { Name = "Small", Sku = "CT-S", Quantity = 5, ProductId = product.Id });
            context.SaveChanges();

            var response = await _client.GetAsync("/api/products");
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("Apparel"));
            Assert.That(body, Does.Contain("Small"));
        }

        private class ProductResponse
        {
            public int Id { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public List<VariantResponse> Variants { get; set; } = new();
        }

        private class VariantResponse
        {
            public string Name { get; set; } = string.Empty;
        }
    }
}