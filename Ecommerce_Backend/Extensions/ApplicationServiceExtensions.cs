using Ecommerce_Backend.Services;

namespace Ecommerce_Backend.Extensions
{
    public static class ApplicationServiceExtensions
    {
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IVariantService, VariantService>();

            return services;
        }
    }
}
