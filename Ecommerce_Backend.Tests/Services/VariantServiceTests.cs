using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Tests.Services
{
    [TestFixture]
    public class VariantServiceTests
    {
        private AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static Product CreateProduct(decimal basePrice = 100)
        {
            return new Product
            {
                Name = "Cotton Top",
                BasePrice = basePrice,
                CategoryId = 0
            };
        }

        [Test]
        public async Task GetVariantByIdAsync_WithExistingId_ReturnsDtoWithProductInfo()
        {
            using var context = CreateContext();
            var product = CreateProduct(basePrice: 100);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var variant = new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            context.Variants.Add(variant);
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var dto = await service.GetVariantByIdAsync(variant.Id);

            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.Name, Is.EqualTo("Small"));
            Assert.That(dto.Sku, Is.EqualTo("TOP-S"));
            Assert.That(dto.ProductId, Is.EqualTo(product.Id));
            Assert.That(dto.ProductName, Is.EqualTo("Cotton Top"));
        }

        [Test]
        public async Task GetVariantByIdAsync_WithNonExistingId_ReturnsNull()
        {
            using var context = CreateContext();
            var service = new VariantService(context);

            var dto = await service.GetVariantByIdAsync(9999);

            Assert.That(dto, Is.Null);
        }

        [Test]
        public async Task GetVariantByIdAsync_WithNoOverridePrice_FallsBackToProductBasePrice()
        {
            using var context = CreateContext();
            var product = CreateProduct(basePrice: 150);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var variant = new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };
            context.Variants.Add(variant);
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var dto = await service.GetVariantByIdAsync(variant.Id);

            Assert.That(dto!.Price, Is.EqualTo(150));
        }

        [Test]
        public async Task GetVariantByIdAsync_WithOverridePrice_UsesVariantPrice()
        {
            using var context = CreateContext();
            var product = CreateProduct(basePrice: 150);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var variant = new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, Price = 120, ProductId = product.Id };
            context.Variants.Add(variant);
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var dto = await service.GetVariantByIdAsync(variant.Id);

            Assert.That(dto!.Price, Is.EqualTo(120));
        }

        [TestCase(0, "OUT_OF_STOCK")]
        [TestCase(4, "LOW_STOCK")]
        [TestCase(5, "IN_STOCK")]
        public async Task GetVariantByIdAsync_ComputesStockStatusFromQuantity(int quantity, string expectedStatus)
        {
            using var context = CreateContext();
            var product = CreateProduct();
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var variant = new Variant { Name = "Small", Sku = "TOP-S", Quantity = quantity, ProductId = product.Id };
            context.Variants.Add(variant);
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var dto = await service.GetVariantByIdAsync(variant.Id);

            Assert.That(dto!.StockStatus, Is.EqualTo(expectedStatus));
        }

        [Test]
        public async Task GetAllVariantsAsync_WithoutFilter_ReturnsAllVariants()
        {
            using var context = CreateContext();
            var product = CreateProduct();
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.Variants.AddRange(
                new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id },
                new Variant { Name = "Medium", Sku = "TOP-M", Quantity = 5, ProductId = product.Id });
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var result = await service.GetAllVariantsAsync();

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task GetAllVariantsAsync_WithProductIdFilter_ReturnsOnlyMatchingVariants()
        {
            using var context = CreateContext();
            var productA = CreateProduct();
            var productB = CreateProduct();
            context.Products.AddRange(productA, productB);
            await context.SaveChangesAsync();

            context.Variants.AddRange(
                new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = productA.Id },
                new Variant { Name = "Only B", Sku = "BOT-M", Quantity = 5, ProductId = productB.Id });
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var result = await service.GetAllVariantsAsync(productA.Id);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Sku, Is.EqualTo("TOP-S"));
        }

        [Test]
        public async Task CreateVariantAsync_WithExistingProduct_SavesVariant()
        {
            using var context = CreateContext();
            var product = CreateProduct();
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var variant = new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id };

            var created = await service.CreateVariantAsync(variant);

            Assert.That(created.Id, Is.Not.EqualTo(0));
            Assert.That(context.Variants.Count(), Is.EqualTo(1));
        }

        [Test]
        public void CreateVariantAsync_WithNonExistingProduct_ThrowsInvalidOperationException()
        {
            using var context = CreateContext();
            var service = new VariantService(context);
            var variant = new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = 9999 };

            Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateVariantAsync(variant));
        }

        [Test]
        public async Task CreateVariantAsync_WithDuplicateSku_ThrowsInvalidOperationException()
        {
            using var context = CreateContext();
            var product = CreateProduct();
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.Variants.Add(new Variant { Name = "Small", Sku = "TOP-S", Quantity = 10, ProductId = product.Id });
            await context.SaveChangesAsync();

            var service = new VariantService(context);
            var duplicate = new Variant { Name = "Large", Sku = "TOP-S", Quantity = 2, ProductId = product.Id };

            Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateVariantAsync(duplicate));
        }
    }
}
