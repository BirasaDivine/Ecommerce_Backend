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
    public class CategoriesControllerTests
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

        [Test]
        public async Task GetAllCategories_ReturnsAllSeededCategories()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            context.Categories.AddRange(
                new Category { Name = "Women" },
                new Category { Name = "Men" });
            context.SaveChanges();

            var response = await _client.GetAsync("/api/categories");
            var categories = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(categories, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task GetCategoryById_WithNonExistingId_Returns404()
        {
            var response = await _client.GetAsync("/api/categories/9999");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task GetCategoryById_WithExistingId_Returns200()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Name = "Women" };
            context.Categories.Add(category);
            context.SaveChanges();

            var response = await _client.GetAsync($"/api/categories/{category.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task CreateCategory_WithValidData_Returns201()
        {
            var payload = new { Name = "Women" };
            var response = await _client.PostAsJsonAsync("/api/categories", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        [Test]
        public async Task CreateCategory_WithEmptyName_Returns400()
        {
            var payload = new { Name = "" };
            var response = await _client.PostAsJsonAsync("/api/categories", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateCategory_WithDuplicateNameAtSameLevel_Returns400()
        {
            var first = new { Name = "Women" };
            await _client.PostAsJsonAsync("/api/categories", first);

            var duplicate = new { Name = "Women" };
            var response = await _client.PostAsJsonAsync("/api/categories", duplicate);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        private class CategoryResponse
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
