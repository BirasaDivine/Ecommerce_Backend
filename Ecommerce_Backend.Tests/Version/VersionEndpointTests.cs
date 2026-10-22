using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using System.Net;
using System.Text.Json;

namespace Ecommerce_Backend.Tests.Version
{
    [TestFixture]
    public class VersionEndpointTests
    {
        [Test]
        public async Task Version_ReportsTheConfiguredCommitWithoutAuthentication()
        {
            using var factory = new CustomWebApplicationFactory(enableOrderProcessing: false)
                .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { ["Version:Commit"] = "abc1234" })));
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/version");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.That(document.RootElement.GetProperty("commit").GetString(), Is.EqualTo("abc1234"));
        }

        [Test]
        public async Task Version_DefaultsToUnknownWhenNoCommitWasBuiltIn()
        {
            using var factory = new CustomWebApplicationFactory(enableOrderProcessing: false);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/version");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.That(document.RootElement.GetProperty("commit").GetString(), Is.EqualTo("unknown"));
        }
    }
}
