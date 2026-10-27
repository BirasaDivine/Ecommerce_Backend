using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ecommerce_Backend.Data;

namespace Ecommerce_Backend.Tests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        internal const string EmulatorConnectionString =
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

        // Dedicated queue so a running api/consumer container never competes with the tests for messages.
        internal const string TestQueueName = "order-placed-tests";

        static CustomWebApplicationFactory()
        {
            // The exporter is chosen while Program builds the host, before in-memory settings apply, so it
            // comes from the environment. None keeps span and metric output out of the test log; tests that
            // need spans add their own in-memory exporter.
            Environment.SetEnvironmentVariable("Telemetry__Exporter", "None");
        }

        private readonly string _databaseName;
        private readonly bool _enableOrderProcessing;

        public CustomWebApplicationFactory(string? databaseName = null, bool enableOrderProcessing = true)
        {
            _databaseName = databaseName ?? "TestDb_" + Guid.NewGuid();
            _enableOrderProcessing = enableOrderProcessing;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ServiceBus:ConnectionString"] = EmulatorConnectionString,
                    ["ServiceBus:QueueName"] = TestQueueName
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

                if (!_enableOrderProcessing)
                {
                    services.RemoveAll(typeof(IHostedService));
                }
            });
        }
    }
}