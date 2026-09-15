using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace SingularFlow.Api.Tests.Health;

public sealed class HealthEndpointTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(
        WebApplicationFactory<Program> factory)
    {
        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        _client = factory.CreateClient(options);
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyResponse()
    {
        HttpResponseMessage response =
            await _client.GetAsync("/health");

        string content =
            await response.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            "Healthy",
            content);
    }
}