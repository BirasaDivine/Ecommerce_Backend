using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Tests.Services
{
    [TestFixture]
    public class CategoryServiceTests
    {
        private AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Test]
        public async Task GetCategoryByIdAsync_WithNonExistingId_ReturnsNull()
        {
            using var context = CreateContext();
            var service = new CategoryService(context);

            var dto = await service.GetCategoryByIdAsync(9999);

            Assert.That(dto, Is.Null);
        }

        [Test]
        public async Task GetCategoryByIdAsync_WithTopLevelCategory_ReturnsDtoWithNullParent()
        {
            using var context = CreateContext();
            var category = new Category { Name = "Women" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var service = new CategoryService(context);
            var dto = await service.GetCategoryByIdAsync(category.Id);

            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.Name, Is.EqualTo("Women"));
            Assert.That(dto.ParentCategoryId, Is.Null);
            Assert.That(dto.ParentCategoryName, Is.Null);
        }

        [Test]
        public async Task GetCategoryByIdAsync_WithChildCategory_ReturnsDtoWithParentInfo()
        {
            using var context = CreateContext();
            var parent = new Category { Name = "Women" };
            context.Categories.Add(parent);
            await context.SaveChangesAsync();

            var child = new Category { Name = "Dresses", ParentCategoryId = parent.Id };
            context.Categories.Add(child);
            await context.SaveChangesAsync();

            var service = new CategoryService(context);
            var dto = await service.GetCategoryByIdAsync(child.Id);

            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.ParentCategoryId, Is.EqualTo(parent.Id));
            Assert.That(dto.ParentCategoryName, Is.EqualTo("Women"));
        }

        [Test]
        public async Task GetAllCategoriesAsync_ReturnsAllCategoriesWithParentNames()
        {
            using var context = CreateContext();
            var parent = new Category { Name = "Women" };
            context.Categories.Add(parent);
            await context.SaveChangesAsync();

            context.Categories.AddRange(
                new Category { Name = "Dresses", ParentCategoryId = parent.Id },
                new Category { Name = "Men" });
            await context.SaveChangesAsync();

            var service = new CategoryService(context);
            var result = await service.GetAllCategoriesAsync();

            Assert.That(result, Has.Count.EqualTo(3));
            var dresses = result.Single(c => c.Name == "Dresses");
            Assert.That(dresses.ParentCategoryName, Is.EqualTo("Women"));
        }

        [Test]
        public async Task CreateCategoryAsync_WithValidData_SavesCategory()
        {
            using var context = CreateContext();
            var service = new CategoryService(context);
            var category = new Category { Name = "Women" };

            var created = await service.CreateCategoryAsync(category);

            Assert.That(created.Id, Is.Not.EqualTo(0));
            Assert.That(context.Categories.Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task CreateCategoryAsync_WithDuplicateNameAtSameLevel_ThrowsInvalidOperationException()
        {
            using var context = CreateContext();
            var parent = new Category { Name = "Women" };
            context.Categories.Add(parent);
            await context.SaveChangesAsync();

            context.Categories.Add(new Category { Name = "Dresses", ParentCategoryId = parent.Id });
            await context.SaveChangesAsync();

            var service = new CategoryService(context);
            var duplicate = new Category { Name = "Dresses", ParentCategoryId = parent.Id };

            Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCategoryAsync(duplicate));
        }

        [Test]
        public async Task CreateCategoryAsync_WithSameNameAtDifferentLevels_Succeeds()
        {
            using var context = CreateContext();
            var women = new Category { Name = "Women" };
            var men = new Category { Name = "Men" };
            context.Categories.AddRange(women, men);
            await context.SaveChangesAsync();

            context.Categories.Add(new Category { Name = "Shoes", ParentCategoryId = women.Id });
            await context.SaveChangesAsync();

            var service = new CategoryService(context);
            var underMen = new Category { Name = "Shoes", ParentCategoryId = men.Id };

            var created = await service.CreateCategoryAsync(underMen);

            Assert.That(created.Id, Is.Not.EqualTo(0));
            Assert.That(context.Categories.Count(c => c.Name == "Shoes"), Is.EqualTo(2));
        }
    }
}
