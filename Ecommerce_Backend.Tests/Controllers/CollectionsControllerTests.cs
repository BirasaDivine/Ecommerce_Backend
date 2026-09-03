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
    public class CollectionsControllerTests
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

        private async Task AuthenticateAsAdminAsync()
        {
            var token = await GetAdminTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        private Product SeedProduct()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

            var product = new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id };
            context.Products.Add(product);
            context.SaveChanges();

            return product;
        }

        [Test]
        public async Task CreateCollection_WithoutAuth_Returns401()
        {
            var response = await _client.PostAsJsonAsync("/api/collections", new { Name = "Summer Sale" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task CreateCollection_AsNonAdmin_Returns403()
        {
            var token = await GetUserTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/collections", new { Name = "Summer Sale" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task CreateCollection_AsAdmin_Returns201()
        {
            await AuthenticateAsAdminAsync();

            var response = await _client.PostAsJsonAsync("/api/collections", new { Name = "Summer Sale" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        [Test]
        public async Task AddProductToCollection_AsAdmin_AddsProduct()
        {
            await AuthenticateAsAdminAsync();
            var product = SeedProduct();

            var createResponse = await _client.PostAsJsonAsync("/api/collections", new { Name = "Summer Sale" });
            var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();

            var response = await _client.PostAsJsonAsync($"/api/collections/{collection!.Id}/products",
                new { ProductId = product.Id });
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("Cotton Top"));
        }

        [Test]
        public async Task AddProductToCollection_Twice_Returns400()
        {
            await AuthenticateAsAdminAsync();
            var product = SeedProduct();

            var createResponse = await _client.PostAsJsonAsync("/api/collections", new { Name = "Summer Sale" });
            var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();

            await _client.PostAsJsonAsync($"/api/collections/{collection!.Id}/products", new { ProductId = product.Id });
            var response = await _client.PostAsJsonAsync($"/api/collections/{collection.Id}/products", new { ProductId = product.Id });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task AddProductToCollection_NonExistentProduct_Returns404()
        {
            await AuthenticateAsAdminAsync();

            var createResponse = await _client.PostAsJsonAsync("/api/collections", new { Name = "Summer Sale" });
            var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();

            var response = await _client.PostAsJsonAsync($"/api/collections/{collection!.Id}/products", new { ProductId = 9999 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task ProductCanBelongToMultipleCollectionsSimultaneously()
        {
            await AuthenticateAsAdminAsync();
            var product = SeedProduct();

            var firstResponse = await _client.PostAsJsonAsync("/api/collections", new { Name = "Summer Sale" });
            var first = await firstResponse.Content.ReadFromJsonAsync<Collection>();

            var secondResponse = await _client.PostAsJsonAsync("/api/collections", new { Name = "New Arrivals" });
            var second = await secondResponse.Content.ReadFromJsonAsync<Collection>();

            var addFirst = await _client.PostAsJsonAsync($"/api/collections/{first!.Id}/products", new { ProductId = product.Id });
            var addSecond = await _client.PostAsJsonAsync($"/api/collections/{second!.Id}/products", new { ProductId = product.Id });

            Assert.That(addFirst.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(addSecond.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var firstDetail = await _client.GetAsync($"/api/collections/{first.Id}");
            var secondDetail = await _client.GetAsync($"/api/collections/{second.Id}");

            Assert.That(await firstDetail.Content.ReadAsStringAsync(), Does.Contain("Cotton Top"));
            Assert.That(await secondDetail.Content.ReadAsStringAsync(), Does.Contain("Cotton Top"));
        }

        [Test]
        public async Task GetCollectionById_WithNonExistingId_Returns404()
        {
            var response = await _client.GetAsync("/api/collections/9999");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }
}
