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

    }
}