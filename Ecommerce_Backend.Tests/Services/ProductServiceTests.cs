using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Tests.Services
{
    [TestFixture]
    public class ProductServiceTests
    {
        private AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Test]
        public async Task CreateProductAsync_WithValidVariants_SavesProductAndVariants()
        {
            using var context = CreateContext();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var service = new ProductService(context);
            var product = new Product
            {
                Name = "Cotton Top",
                Description = "A lightweight top.",
                BasePrice = 100,
                Material = "Cotton",
                CategoryId = category.Id,
                Variants = new List<Variant>
                {
                    new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10 },
                    new Variant { Name = "Medium", Sku = "TOP-M", Quantity = 5 },
                }
            };

            var created = await service.CreateProductAsync(product);

            Assert.That(created.Id, Is.Not.EqualTo(0));
            Assert.That(context.Products.Count(), Is.EqualTo(1));
            Assert.That(context.Variants.Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task CreateProductAsync_WithDuplicateSku_RollsBackEntireSave()
        {
            using var context = CreateContext();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var service = new ProductService(context);

            // pre-existing variant already in the database with SKU "TOP-S"
            var existingProduct = new Product
            {
                Name = "Existing Top",
                BasePrice = 50,
                CategoryId = category.Id,
                Variants = new List<Variant>
                {
                    new Variant { Name = "Small", Sku = "TOP-S", Quantity = 3 }
                }
            };
            await service.CreateProductAsync(existingProduct);

            // new product tries to reuse "TOP-S" on one of its variants
            var newProduct = new Product
            {
                Name = "New Top",
                BasePrice = 100,
                CategoryId = category.Id,
                Variants = new List<Variant>
                {
                    new Variant { Name = "Medium", Sku = "TOP-M", Quantity = 5 },  // valid
                    new Variant { Name = "Large", Sku = "TOP-S", Quantity = 2 },   // duplicate!
                }
            };

            Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateProductAsync(newProduct));

            // the whole newProduct save should have rolled back —
            // only the original pre-existing product should exist
            Assert.That(context.Products.Count(), Is.EqualTo(1));
            Assert.That(context.Variants.Count(), Is.EqualTo(1));
            Assert.That(context.Variants.Any(v => v.Sku == "TOP-M"), Is.False);
        }

        [Test]
        public async Task GetProductByIdAsync_WithNonExistingId_ReturnsNull()
        {
            using var context = CreateContext();
            var service = new ProductService(context);

            var dto = await service.GetProductByIdAsync(9999);

            Assert.That(dto, Is.Null);
        }

        [Test]
        public async Task GetProductByIdAsync_WithExistingId_ReturnsDtoWithCategoryName()
        {
            using var context = CreateContext();
            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Cotton Top", BasePrice = 100, CategoryId = category.Id };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var service = new ProductService(context);
            var dto = await service.GetProductByIdAsync(product.Id);

            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.Name, Is.EqualTo("Cotton Top"));
            Assert.That(dto.CategoryName, Is.EqualTo("Topwear"));
        }

        [Test]
        public async Task GetProductByIdAsync_OnlyIncludesActiveVariants()
        {
            using var context = CreateContext();
            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

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
            await context.SaveChangesAsync();

            var service = new ProductService(context);
            var dto = await service.GetProductByIdAsync(product.Id);

            Assert.That(dto!.Variants, Has.Count.EqualTo(1));
            Assert.That(dto.Variants[0].Name, Is.EqualTo("Small"));
        }

        [Test]
        public async Task GetProductByIdAsync_VariantWithNoOverridePrice_FallsBackToProductBasePrice()
        {
            using var context = CreateContext();
            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Name = "Cotton Top",
                BasePrice = 150,
                CategoryId = category.Id,
                Variants = new List<Variant>
                {
                    new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10 },
                }
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var service = new ProductService(context);
            var dto = await service.GetProductByIdAsync(product.Id);

            Assert.That(dto!.Variants[0].Price, Is.EqualTo(150));
        }

        [TestCase(0, "OUT_OF_STOCK")]
        [TestCase(4, "LOW_STOCK")]
        [TestCase(5, "IN_STOCK")]
        public async Task GetProductByIdAsync_ComputesVariantStockStatusFromQuantity(int quantity, string expectedStatus)
        {
            using var context = CreateContext();
            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Name = "Cotton Top",
                BasePrice = 100,
                CategoryId = category.Id,
                Variants = new List<Variant>
                {
                    new Variant { Name = "Small", Sku = "TOP-S", Quantity = quantity },
                }
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var service = new ProductService(context);
            var dto = await service.GetProductByIdAsync(product.Id);

            Assert.That(dto!.Variants[0].StockStatus, Is.EqualTo(expectedStatus));
        }
    }
}
