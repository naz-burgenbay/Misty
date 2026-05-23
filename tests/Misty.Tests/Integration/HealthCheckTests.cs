using System.Net;
using FluentAssertions;

namespace Misty.Tests.Integration;

// SQL, Redis, and WebApplicationFactory are shared via ApiFactory (IntegrationCollection fixture). Service Bus is stubbed in ApiFactory
[Collection("Integration")]
public sealed class HealthCheckTests
{
    private readonly HttpClient _client;

    public HealthCheckTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
