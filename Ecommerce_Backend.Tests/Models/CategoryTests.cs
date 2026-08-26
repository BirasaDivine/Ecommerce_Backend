using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Ecommerce_Backend.Tests.Models
{
    [TestFixture]
    public class CategoryTests
    {
        [Test]
        public void Category_WithEmptyName_FailsValidation()
        {
            var category = new Category { Name = "" };

            var validationContext = new ValidationContext(category);
            var validationResults = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(category, validationContext, validationResults, validateAllProperties: true);

            Assert.That(isValid, Is.False);
        }
        [Test]
        public void Category_WithValidName_PassesValidation()
        {
            var category = new Category { Name = "Dresses" };

            var validationContext = new ValidationContext(category);
            var validationResults = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(category, validationContext, validationResults, validateAllProperties: true);

            Assert.That(isValid, Is.True);
        }
        [Test]
        public void AddCategory_WithDuplicateNameAtSameLevel_ThrowsException()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            using var context = new AppDbContext(options);
            var parent = new Category { Name = "Women" };
            context.Categories.Add(parent);
            context.SaveChanges();

            var firstChild = new Category { Name = "Dresses", ParentCategoryId = parent.Id };
            context.Categories.Add(firstChild);
            context.SaveChanges();

            var duplicateChild = new Category { Name = "Dresses", ParentCategoryId = parent.Id };

            Assert.Throws<InvalidOperationException>(() =>
            {
                context.Categories.Add(duplicateChild);
                context.SaveChanges();
            });
        }
    }
}
