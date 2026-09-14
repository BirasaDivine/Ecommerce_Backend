using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ecommerce_Backend.Data;
using Ecommerce_Backend.Messaging;

namespace Ecommerce_Backend.Tests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        // The well-known, non-secret connection string the Azure Service Bus
        // emulator (docker-compose.yml at the repo root) accepts.
        private const string EmulatorConnectionString =
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

        private readonly string _databaseName;

        public CustomWebApplicationFactory(string? databaseName = null)
        {
            _databaseName = databaseName ?? "TestDb_" + Guid.NewGuid();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ServiceBus:ConnectionString"] = EmulatorConnectionString
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                services.RemoveAll(typeof(AppDbContext));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_databaseName);
                });
            });
        }

        // Lets a test simulate the consumer being down: orders are inserted and
        // published, but nothing drains the queue until a new processor starts.
        public async Task StopOrderProcessingAsync()
        {
            var hostedServices = Services.GetServices<IHostedService>();
            foreach (var service in hostedServices.OfType<OrderProcessingService>())
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }
}