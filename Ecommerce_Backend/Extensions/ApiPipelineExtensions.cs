using Scalar.AspNetCore;

namespace Ecommerce_Backend.Extensions
{
    public static class ApiPipelineExtensions
    {
        public static async Task UseApiPipelineAsync(this WebApplication app)
        {
            await app.SeedDevelopmentDataAsync();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference();
            }

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
        }
    }
}
