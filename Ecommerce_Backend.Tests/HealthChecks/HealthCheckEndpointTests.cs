using Ecommerce_Backend.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Ecommerce_Backend.Tests.HealthChecks
{
    [TestFixture]
    public class HealthCheckEndpointTests
    {
        private CustomWebApplicationFactory _factory;
        private HttpClient _client;

        [SetUp]
        public void Setup()
        {
            _factory = new CustomWebApplicationFactory(enableOrderProcessing: false);
            _client = _factory.CreateClient();
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
            _factory?.Dispose();
        }

        private static async Task<JsonElement> ReadChecksAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("checks").Clone();
        }

        [Test]
        public async Task Live_ReturnsHealthyWithoutCheckingDependencies()
        {
            var response = await _client.GetAsync("/health/live");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var checks = await ReadChecksAsync(response);
            Assert.That(checks.EnumerateObject().Select(c => c.Name), Is.EquivalentTo(new[] { "self" }));
        }

        [Test]
        public async Task Ready_RunsOnlyDependencyChecks()
        {
            var response = await _client.GetAsync("/health/ready");

            var checks = await ReadChecksAsync(response);
            Assert.That(checks.EnumerateObject().Select(c => c.Name),
                Is.EquivalentTo(new[] { "database", "servicebus-order-queue" }));
            Assert.That(checks.GetProperty("database").GetProperty("status").GetString(), Is.EqualTo("Healthy"));
        }

        [TestCase("Jwt:Key", "too-short", "Jwt:Key")]
        [TestCase("HealthChecks:TimeoutSeconds", "0", "HealthChecks:TimeoutSeconds")]
        [TestCase("Database:ConnectionString", "", "Database:ConnectionString")]
        public void InvalidConfiguration_FailsAtStartup(string key, string value, string expectedMessageFragment)
        {
            using var factory = new CustomWebApplicationFactory(enableOrderProcessing: false)
                .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));

            var exception = Assert.Catch(() => factory.CreateClient());

            Assert.That(exception!.ToString(), Does.Contain(expectedMessageFragment));
        }
    }
}
