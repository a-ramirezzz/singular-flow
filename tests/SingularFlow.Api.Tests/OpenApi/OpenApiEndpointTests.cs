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
}