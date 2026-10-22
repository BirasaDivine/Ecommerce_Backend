using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class DatabaseServiceExtensions
    {
        public static IServiceCollection AddAppDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<DatabaseOptions>()
                .Bind(configuration.GetSection(DatabaseOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddDbContext<AppDbContext>((sp, options) =>
                options.UseSqlServer(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

            return services;
        }
    }
}
