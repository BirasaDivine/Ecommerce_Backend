namespace Ecommerce_Backend.Extensions
{
    public static class ApiServiceExtensions
    {
        // Everything only the HTTP API needs; the consumer role skips all of this.
        public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddControllers();
            services.AddOpenApi();
            services.AddAppAuthentication(configuration);
            services.AddAppServices();
            services.AddDevelopmentSeedOptions(configuration);

            return services;
        }
    }
}
