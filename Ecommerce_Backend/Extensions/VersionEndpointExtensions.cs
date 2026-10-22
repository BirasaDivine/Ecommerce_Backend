using Ecommerce_Backend.Configuration;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class VersionEndpointExtensions
    {
        public static IServiceCollection AddAppVersion(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<VersionOptions>()
                .Bind(configuration.GetSection(VersionOptions.Path))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            return services;
        }

        public static WebApplication MapAppVersion(this WebApplication app)
        {
            app.MapGet("/version", (IOptions<VersionOptions> options) => Results.Ok(new { commit = options.Value.Commit }))
                .AllowAnonymous();

            return app;
        }
    }
}
