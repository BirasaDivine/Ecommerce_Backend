using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class DatabaseMigrationExtensions
    {
        public static async Task ApplyDatabaseMigrationsAsync(this WebApplication app)
        {
            var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            if (!options.MigrateOnStartup)
            {
                return;
            }

            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!context.Database.IsRelational())
            {
                return;
            }

            app.Logger.LogInformation("Applying database migrations on startup.");
            await context.Database.MigrateAsync();
        }
    }
}
