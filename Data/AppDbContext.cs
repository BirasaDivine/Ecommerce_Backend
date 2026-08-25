using Microsoft.EntityFrameworkCore;
using Ecommerce_Backend.Models;

namespace Ecommerce_Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }

        public override int SaveChanges()
        {
            ValidateCategoryNameUniqueness();
            return base.SaveChanges();
        }

        private void ValidateCategoryNameUniqueness()
        {
            var newOrChangedCategories = ChangeTracker.Entries<Category>()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                .Select(e => e.Entity);

            foreach (var category in newOrChangedCategories)
            {
                bool duplicateExists = Categories.Local
                    .Concat(Categories)
                    .Any(c => c != category
                        && c.Name == category.Name
                        && c.ParentCategoryId == category.ParentCategoryId);

                if (duplicateExists)
                {
                    throw new InvalidOperationException(
                        $"A category named '{category.Name}' already exists at this level.");
                }
            }
        }
    }
}