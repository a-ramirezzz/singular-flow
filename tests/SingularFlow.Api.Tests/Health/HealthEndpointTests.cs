using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace SingularFlow.Api.Tests.Health;

public sealed class HealthEndpointTests :
    IClassFixture<SingularFlowApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(
        SingularFlowApiFactory factory)
    {
        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        _client = factory.CreateClient(options);
    }

    [Fact]
    public async Task GetLiveness_ReturnsHealthyResponse()
    {
        using HttpResponseMessage response =
            await _client.GetAsync(
                "/health/live");

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