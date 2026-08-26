using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

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
        public void CreateProduct_WithValidVariants_SavesProductAndVariants()
        {
            using var context = CreateContext();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

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

            context.Products.Add(product);
            context.SaveChanges();

            Assert.That(context.Products.Count(), Is.EqualTo(1));
            Assert.That(context.Variants.Count(), Is.EqualTo(2));
        }

        [Test]
        public void CreateProduct_WithDuplicateSku_RollsBackEntireSave()
        {
            using var context = CreateContext();

            var category = new Category { Name = "Topwear" };
            context.Categories.Add(category);
            context.SaveChanges();

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
            context.Products.Add(existingProduct);
            context.SaveChanges();

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

            Assert.Throws<InvalidOperationException>(() =>
            {
                context.Products.Add(newProduct);
                context.SaveChanges();
            });

            // the whole newProduct save should have rolled back —
            // only the original pre-existing product should exist
            Assert.That(context.Products.Count(), Is.EqualTo(1));
            Assert.That(context.Variants.Count(), Is.EqualTo(1));
            Assert.That(context.Variants.Any(v => v.Sku == "TOP-M"), Is.False);
        }
    }
}
