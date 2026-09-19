using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationEndpointTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SimulationEndpointTests(
        WebApplicationFactory<Program> factory)
    {
        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        _client = factory.CreateClient(options);
    }

    [Fact]
    public async Task PostSimulation_WithValidLogarithmicRequest_ReturnsCalculatedSeries()
    {
        var request = new
        {
            singularTime = 1.0,
            concentrationExponent = 0.005,
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
            HttpStatusCode.OK,
            response.StatusCode);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(contentStream);

        JsonElement root = document.RootElement;
        JsonElement states = root.GetProperty("states");

        Assert.Equal(
            1.0,
            root.GetProperty("singularTime").GetDouble());

        Assert.Equal(
            0.005,
            root.GetProperty(
                "concentrationExponent").GetDouble());

        Assert.Equal(
            "logarithmic",
            root.GetProperty("samplingMode").GetString());

        Assert.Equal(
            6,
            states.GetArrayLength());

        JsonElement firstState = states[0];
        JsonElement lastState = states[5];

        Assert.Equal(
            0.0,
            firstState.GetProperty("time").GetDouble());

        Assert.Equal(
            0.9999,
            lastState.GetProperty("time").GetDouble(),
            precision: 10);

        Assert.True(
            lastState
                .GetProperty("angularVelocityScale")
                .GetDouble() >
            firstState
                .GetProperty("angularVelocityScale")
                .GetDouble());
    }
    [Fact]
    public async Task PostSimulation_WithValidUniformRequest_ReturnsUniformSeries()
    {
        var request = new
        {
            singularTime = 1.0,
            concentrationExponent = 0.005,
            startTime = 0.0,
            endTime = 0.8,
            sampleCount = 5,
            samplingMode = "uniform"
        };

        using HttpResponseMessage response =
            await _client.PostAsJsonAsync(
                "/api/simulations",
                request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(contentStream);

        JsonElement root = document.RootElement;
        JsonElement states = root.GetProperty("states");

        Assert.Equal(
            "uniform",
            root.GetProperty("samplingMode").GetString());

        Assert.Equal(
            5,
            states.GetArrayLength());

        double[] expectedTimes =
        [
            0.0,
        0.2,
        0.4,
        0.6,
        0.8
        ];

        for (int index = 0;
             index < expectedTimes.Length;
             index++)
        {
            Assert.Equal(
                expectedTimes[index],
                states[index]
                    .GetProperty("time")
                    .GetDouble(),
                precision: 10);
        }
    }
}