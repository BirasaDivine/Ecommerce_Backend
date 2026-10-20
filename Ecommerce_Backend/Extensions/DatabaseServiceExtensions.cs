using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Extensions
{
    public static class DatabaseServiceExtensions
    {
        public const string ConnectionStringName = "DefaultConnection";

        public static IServiceCollection AddAppDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} is not set. Provide it via appsettings, user secrets or the ConnectionStrings__{ConnectionStringName} environment variable.");
            }

            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

            services.AddOptions<DatabaseOptions>()
                .Bind(configuration.GetSection(DatabaseOptions.Path));

            return services;
        }
    }
}
