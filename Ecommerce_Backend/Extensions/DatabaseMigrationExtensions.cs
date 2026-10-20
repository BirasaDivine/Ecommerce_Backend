using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class DatabaseMigrationExtensions
    {
        // Applies pending EF Core migrations when Database:MigrateOnStartup is true.
        // On SQL Server, EF Core takes a database-level lock while migrating, so
        // several instances starting at once won't apply the same migration twice.
        public static async Task ApplyDatabaseMigrationsAsync(this WebApplication app)
        {
            var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            if (!options.MigrateOnStartup)
            {
                return;
            }

            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Non-relational providers (e.g. the in-memory database used by tests)
            // have no migrations to apply.
            if (!context.Database.IsRelational())
            {
                return;
            }

            app.Logger.LogInformation("Applying database migrations on startup.");
            await context.Database.MigrateAsync();
        }
    }
}
