using Ecommerce_Backend.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Net;

namespace Ecommerce_Backend.Tests.Roles
{
    [TestFixture]
    public class AppRoleTests
    {
        // The roles are read while Program.cs builds the host, before WebApplicationFactory
        // applies in-memory settings, so (like compose does) set them as environment variables.
        private const string ApiVariable = "Api__Enabled";
        private const string ConsumerVariable = "Consumer__Enabled";

        private CustomWebApplicationFactory? _factory;

        [TearDown]
        public void TearDown()
        {
            _factory?.Dispose();
            Environment.SetEnvironmentVariable(ApiVariable, null);
            Environment.SetEnvironmentVariable(ConsumerVariable, null);
        }

        private CustomWebApplicationFactory CreateFactory(bool apiEnabled, bool consumerEnabled)
        {
            Environment.SetEnvironmentVariable(ApiVariable, apiEnabled.ToString());
            Environment.SetEnvironmentVariable(ConsumerVariable, consumerEnabled.ToString());
            _factory = new CustomWebApplicationFactory();
            return _factory;
        }

        [Test]
        public void BothRolesDisabled_FailsAtStartup()
        {
            var factory = CreateFactory(apiEnabled: false, consumerEnabled: false);

            var exception = Assert.Catch(() => factory.CreateClient());

            Assert.That(exception!.ToString(), Does.Contain("Api:Enabled").And.Contain("Consumer:Enabled"));
        }

        [Test]
        public async Task ApiOnly_ServesRoutesAndRunsNoConsumer()
        {
            var factory = CreateFactory(apiEnabled: true, consumerEnabled: false);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/products");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Services.GetServices<IHostedService>().OfType<OrderProcessingService>(), Is.Empty);
        }

        [Test]
        public async Task ConsumerOnly_RunsConsumerAndServesNoApiRoutes()
        {
            var factory = CreateFactory(apiEnabled: false, consumerEnabled: true);
            using var client = factory.CreateClient();

            var products = await client.GetAsync("/api/products");
            var live = await client.GetAsync("/health/live");

            Assert.That(products.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Services.GetServices<IHostedService>().OfType<OrderProcessingService>(), Is.Not.Empty);
        }
    }
}
