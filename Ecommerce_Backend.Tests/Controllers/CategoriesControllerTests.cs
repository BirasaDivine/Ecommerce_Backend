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
        public async Task GetCategoryById_WithChildren_ReturnsChildrenList()
        {
            await AuthenticateAsAdminAsync();

            var parentResponse = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });
            var parent = await parentResponse.Content.ReadFromJsonAsync<Category>();

            await _client.PostAsJsonAsync("/api/categories", new { Name = "Dresses", ParentCategoryId = parent!.Id });

            var response = await _client.GetAsync($"/api/categories/{parent.Id}");
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("Dresses"));
        }

        [Test]
        public async Task CreateCategory_WithoutAuth_Returns401()
        {
            var response = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task CreateCategory_AsNonAdmin_Returns403()
        {
            var token = await GetUserTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task CreateCategory_WithValidData_Returns201()
        {
            await AuthenticateAsAdminAsync();

            var payload = new { Name = "Women" };
            var response = await _client.PostAsJsonAsync("/api/categories", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        [Test]
        public async Task CreateCategory_WithEmptyName_Returns400()
        {
            await AuthenticateAsAdminAsync();

            var payload = new { Name = "" };
            var response = await _client.PostAsJsonAsync("/api/categories", payload);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task CreateCategory_WithDuplicateNameAtSameLevel_Returns400()
        {
            await AuthenticateAsAdminAsync();

            await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });
            var response = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task UpdateCategory_WithValidData_Returns200()
        {
            await AuthenticateAsAdminAsync();

            var createResponse = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });
            var category = await createResponse.Content.ReadFromJsonAsync<Category>();

            var response = await _client.PutAsJsonAsync($"/api/categories/{category!.Id}", new { Name = "Womenswear" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task UpdateCategory_SetSelfAsParent_Returns400()
        {
            await AuthenticateAsAdminAsync();

            var createResponse = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });
            var category = await createResponse.Content.ReadFromJsonAsync<Category>();

            var response = await _client.PutAsJsonAsync($"/api/categories/{category!.Id}",
                new { Name = "Women", ParentCategoryId = category.Id });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task UpdateCategory_CreatesCycle_Returns400()
        {
            await AuthenticateAsAdminAsync();

            var parentResponse = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });
            var parent = await parentResponse.Content.ReadFromJsonAsync<Category>();

            var childResponse = await _client.PostAsJsonAsync("/api/categories",
                new { Name = "Dresses", ParentCategoryId = parent!.Id });
            var child = await childResponse.Content.ReadFromJsonAsync<Category>();

            var response = await _client.PutAsJsonAsync($"/api/categories/{parent.Id}",
                new { Name = "Women", ParentCategoryId = child!.Id });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task DeleteCategory_WithSubcategories_Returns400()
        {
            await AuthenticateAsAdminAsync();

            var parentResponse = await _client.PostAsJsonAsync("/api/categories", new { Name = "Women" });
            var parent = await parentResponse.Content.ReadFromJsonAsync<Category>();

            await _client.PostAsJsonAsync("/api/categories", new { Name = "Dresses", ParentCategoryId = parent!.Id });

            var response = await _client.DeleteAsync($"/api/categories/{parent.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task DeleteCategory_WithProductsAssigned_Returns400()
        {
            await AuthenticateAsAdminAsync();

            var categoryResponse = await _client.PostAsJsonAsync("/api/categories", new { Name = "Dresses" });
            var category = await categoryResponse.Content.ReadFromJsonAsync<Category>();

            await _client.PostAsJsonAsync("/api/products",
                new { Name = "Summer Dress", BasePrice = 40, CategoryId = category!.Id });

            var response = await _client.DeleteAsync($"/api/categories/{category.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task DeleteCategory_LeafWithNoProducts_Returns204()
        {
            await AuthenticateAsAdminAsync();

            var categoryResponse = await _client.PostAsJsonAsync("/api/categories", new { Name = "Dresses" });
            var category = await categoryResponse.Content.ReadFromJsonAsync<Category>();

            var response = await _client.DeleteAsync($"/api/categories/{category!.Id}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }

        private class CategoryResponse
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
