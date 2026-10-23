using Ecommerce_Backend.Configuration;

namespace Ecommerce_Backend.Extensions
{
    // Which jobs this process performs. One image runs as the API, the consumer, or (the
    // default, e.g. for dotnet run and the tests) both.
    public sealed record AppRoles(bool ApiEnabled, bool ConsumerEnabled);

    public static class AppRoleExtensions
    {
        public static AppRoles AddAppRoles(this IServiceCollection services, IConfiguration configuration)
        {
            var api = configuration.GetSection(ApiOptions.Path).Get<ApiOptions>() ?? new ApiOptions();
            var consumer = configuration.GetSection(ConsumerOptions.Path).Get<ConsumerOptions>() ?? new ConsumerOptions();

            if (!api.Enabled && !consumer.Enabled)
            {
                throw new InvalidOperationException(
                    $"{ApiOptions.Path}:Enabled and {ConsumerOptions.Path}:Enabled are both false; at least one must be true.");
            }

            services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.Path));
            services.Configure<ConsumerOptions>(configuration.GetSection(ConsumerOptions.Path));

            var roles = new AppRoles(api.Enabled, consumer.Enabled);
            services.AddSingleton(roles);

            return roles;
        }
    }
}
