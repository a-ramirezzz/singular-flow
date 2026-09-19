using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationModelBindingTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SimulationModelBindingTests(
        WebApplicationFactory<Program> factory)
    {
        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        _client = factory.CreateClient(options);
    }

    [Fact]
    public async Task PostSimulation_WithoutSamplingMode_ReturnsValidationProblem()
    {
        var request = new
        {
            singularTime = 1.0,
            concentrationExponent = 0.005,
            startTime = 0.0,
            endTime = 0.9999,
            sampleCount = 6
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
            400,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "One or more validation errors occurred.",
            root.GetProperty("title").GetString());

        JsonElement errors =
            root.GetProperty("errors");

        bool containsSamplingModeError = errors
            .EnumerateObject()
            .Any(property => property.Name.Equals(
                "SamplingMode",
                StringComparison.OrdinalIgnoreCase));

        Assert.True(containsSamplingModeError);
    }

    [Fact]
    public async Task PostSimulation_WithMalformedJson_ReturnsValidationProblem()
    {
        const string malformedJson =
            """
            {
              "singularTime": 1.0,
              "concentrationExponent": 0.005,
            """;

        using StringContent content = new(
            malformedJson,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response =
            await _client.PostAsync(
                "/api/simulations",
                content);

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
            400,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            "One or more validation errors occurred.",
            root.GetProperty("title").GetString());

        Assert.Equal(
            JsonValueKind.Object,
            root.GetProperty("errors").ValueKind);
    }
}