using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace SingularFlow.Api.Tests.OpenApi;

public sealed class OpenApiEndpointTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OpenApiEndpointTests(
        WebApplicationFactory<Program> factory)
    {
        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        _client = factory.CreateClient(options);
    }

    [Fact]
    public async Task GetOpenApiDocument_ReturnsValidDocument()
    {
        using HttpResponseMessage response =
            await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(contentStream);

        JsonElement root = document.RootElement;

        string? openApiVersion =
            root.GetProperty("openapi").GetString();

        string? title = root
            .GetProperty("info")
            .GetProperty("title")
            .GetString();

        Assert.Equal(
            "3.1.1",
            openApiVersion);

        Assert.Equal(
            "SingularFlow.Api | v1",
            title);

        Assert.Equal(
            JsonValueKind.Object,
            root.GetProperty("paths").ValueKind);
    }

    [Fact]
    public async Task GetOpenApiDocument_DocumentsSimulationOperations()
    {
        using HttpResponseMessage response =
            await _client.GetAsync(
                "/openapi/v1.json");

        response.EnsureSuccessStatusCode();

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                contentStream);

        JsonElement paths =
            document.RootElement.GetProperty(
                "paths");

        JsonElement collectionPath =
            paths.GetProperty(
                "/api/simulations");

        JsonElement listOperation =
            collectionPath.GetProperty("get");

        JsonElement createOperation =
            collectionPath.GetProperty("post");

        JsonElement listResponses =
            listOperation.GetProperty(
                "responses");

        Assert.True(
            listResponses.TryGetProperty(
                "200",
                out _));

        Assert.True(
            listResponses.TryGetProperty(
                "400",
                out _));

        string?[] parameterNames =
            listOperation
                .GetProperty("parameters")
                .EnumerateArray()
                .Select(parameter =>
                    parameter.GetProperty("name")
                        .GetString())
                .ToArray();

        Assert.Contains("page", parameterNames);
        Assert.Contains("pageSize", parameterNames);

        JsonElement createResponses =
            createOperation.GetProperty(
                "responses");

        Assert.True(
            createResponses.TryGetProperty(
                "201",
                out _));

        Assert.True(
            createResponses.TryGetProperty(
                "400",
                out _));

        JsonElement resourcePath =
            paths.GetProperty(
                "/api/simulations/{id}");

        JsonElement resourceResponses =
            resourcePath
                .GetProperty("get")
                .GetProperty("responses");

        Assert.True(
            resourceResponses.TryGetProperty(
                "200",
                out _));

        Assert.True(
            resourceResponses.TryGetProperty(
                "404",
                out _));

        JsonElement deleteResponses =
    resourcePath
        .GetProperty("delete")
        .GetProperty("responses");

        Assert.True(
            deleteResponses.TryGetProperty(
                "204",
                out _));

        Assert.True(
            deleteResponses.TryGetProperty(
                "404",
                out _));
    }
}