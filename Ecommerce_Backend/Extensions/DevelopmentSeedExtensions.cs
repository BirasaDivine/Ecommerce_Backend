using Ecommerce_Backend.Configuration;
using Ecommerce_Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Ecommerce_Backend.Extensions
{
    public static class DevelopmentSeedExtensions
    {
        public static IServiceCollection AddDevelopmentSeedOptions(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<SeedAdminOptions>()
                .Bind(configuration.GetSection(SeedAdminOptions.Path))
                .ValidateDataAnnotations();

            return services;
        }

        // Seed roles and a default admin only in Development; a real environment must
        // provision users/roles through a controlled process, not a hard-coded account.
        public static async Task SeedDevelopmentDataAsync(this WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                return;
            }

            using var scope = app.Services.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var seedAdmin = scope.ServiceProvider.GetRequiredService<IOptions<SeedAdminOptions>>().Value;

            string[] roles = { "Admin", "User" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            var existingAdmin = await userManager.FindByEmailAsync(seedAdmin.Email);

            if (existingAdmin == null)
            {
                var admin = new ApplicationUser { UserName = seedAdmin.Email, Email = seedAdmin.Email };
                await userManager.CreateAsync(admin, seedAdmin.Password);
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
    }
}
