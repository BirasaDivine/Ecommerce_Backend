using Ecommerce_Backend.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Variant> Variants { get; set; }
        public DbSet<Collection> Collections { get; set; }
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Quantity doubles as an optimistic-concurrency token so a stock
            // decrement fails loudly (DbUpdateConcurrencyException) instead of
            // silently overselling when two purchases race each other.
            modelBuilder.Entity<Variant>()
                .Property(v => v.Quantity)
                .IsConcurrencyToken();

            // Backstop for ValidateSkuUniqueness below: that check runs before the
            // insert, so it can't catch two concurrent requests racing past it at
            // the same time. The DB constraint is what actually stops that.
            modelBuilder.Entity<Variant>()
                .HasIndex(v => v.Sku)
                .IsUnique();
        }

        public override int SaveChanges()
        {
            ValidateCategoryNameUniqueness();
            ValidateSkuUniqueness();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ValidateCategoryNameUniqueness();
            ValidateSkuUniqueness();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ValidateCategoryNameUniqueness()
        {
            var newOrChangedCategories = ChangeTracker.Entries<Category>()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                .Select(e => e.Entity);

            foreach (var category in newOrChangedCategories)
            {
                bool duplicateInLocal = Categories.Local
                    .Any(c => c != category
                        && c.Name == category.Name
                        && c.ParentCategoryId == category.ParentCategoryId);

                bool duplicateExists = duplicateInLocal || Categories
                    .Any(c => c.Id != category.Id
                        && c.Name == category.Name
                        && c.ParentCategoryId == category.ParentCategoryId);

                if (duplicateExists)
                {
                    throw new InvalidOperationException(
                        $"A category named '{category.Name}' already exists at this level.");
                }
            }
        }

        private void ValidateSkuUniqueness()
        {
            var newOrChangedVariants = ChangeTracker.Entries<Variant>()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                .Select(e => e.Entity);

            foreach (var variant in newOrChangedVariants)
            {
                bool duplicateInLocal = Variants.Local
                    .Any(v => v != variant && v.Sku == variant.Sku);

                bool duplicateExists = duplicateInLocal || Variants
                    .Any(v => v.Id != variant.Id && v.Sku == variant.Sku);

                if (duplicateExists)
                {
                    throw new InvalidOperationException(
                        $"A variant with SKU '{variant.Sku}' already exists.");
                }
            }
        }
    }
}