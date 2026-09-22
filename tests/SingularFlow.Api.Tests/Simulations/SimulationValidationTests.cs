using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Mvc.Testing;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationValidationTests :
    IClassFixture<SimulationEndpointFactory>
{
    private readonly HttpClient _client;

    public SimulationValidationTests(
        SimulationEndpointFactory factory)
    {
        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        _client = factory.CreateClient(options);
    }

    [Fact]
    public async Task PostSimulation_WithUnsupportedSamplingMode_ReturnsBadRequest()
    {
        var request = new
        {
            singularTime = 1.0,
            concentrationExponent = 0.005,
            startTime = 0.0,
            endTime = 0.9999,
            sampleCount = 6,
            samplingMode = "adaptive"
        };

        using HttpResponseMessage response =
            await _client.PostAsJsonAsync(
                "/api/simulations",
                request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(contentStream);

        JsonElement root = document.RootElement;

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Invalid simulation request.",
            root.GetProperty("title").GetString());

        string? detail =
            root.GetProperty("detail").GetString();

        Assert.Contains(
            "sampling mode",
            detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostSimulation_WithInvalidConcentrationExponent_ReturnsBadRequest()
    {
        var request = new
        {
            singularTime = 1.0,
            concentrationExponent = 0.02,
            startTime = 0.0,
            endTime = 0.9999,
            sampleCount = 6,
            samplingMode = "logarithmic"
        };

        using HttpResponseMessage response =
            await _client.PostAsJsonAsync(
                "/api/simulations",
                request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(contentStream);

        JsonElement root = document.RootElement;

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "Invalid simulation request.",
            root.GetProperty("title").GetString());

        string? detail =
            root.GetProperty("detail").GetString();

        Assert.Contains(
            "concentration exponent",
            detail,
            StringComparison.OrdinalIgnoreCase);
    }
}