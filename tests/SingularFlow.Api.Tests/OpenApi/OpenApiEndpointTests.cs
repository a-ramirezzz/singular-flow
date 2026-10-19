using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace SingularFlow.Api.Tests.OpenApi;

public sealed class OpenApiEndpointTests :
    IClassFixture<SingularFlowApiFactory>
{
    private readonly HttpClient _client;

    public OpenApiEndpointTests(
        SingularFlowApiFactory factory)
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

        JsonElement paths =
    root.GetProperty("paths");

        Assert.Equal(
            JsonValueKind.Object,
            paths.ValueKind);

        Assert.False(
            paths.TryGetProperty(
                "/health",
                out _));

        Assert.False(
            paths.TryGetProperty(
                "/health/live",
                out _));

        Assert.False(
            paths.TryGetProperty(
                "/health/ready",
                out _));
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

    [Fact]
    public async Task GetOpenApiDocument_DocumentsSimulationJobOperations()
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

        JsonElement root = document.RootElement;
        JsonElement paths = root.GetProperty("paths");

        JsonElement collectionPath =
            paths.GetProperty(
                "/api/simulation-jobs");
        Assert.Equal(
            ["post"],
            collectionPath.EnumerateObject()
                .Select(property => property.Name)
                .ToArray());

        JsonElement createOperation =
            collectionPath.GetProperty("post");
        JsonElement requestBody =
            createOperation.GetProperty("requestBody");
        Assert.True(
            requestBody.GetProperty("required").GetBoolean());

        JsonElement requestSchema = ResolveSchema(
            root,
            requestBody
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        AssertSchemaProperties(
            root,
            requestSchema,
            new Dictionary<string, string>
            {
                ["singularTime"] = "number",
                ["concentrationExponent"] = "number",
                ["startTime"] = "number",
                ["endTime"] = "number",
                ["sampleCount"] = "integer",
                ["samplingMode"] = "string"
            });

        JsonElement createResponses =
            createOperation.GetProperty("responses");
        Assert.Equal(
            ["202", "400"],
            createResponses.EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray());
        AssertResponseSchema(
            root,
            createResponses.GetProperty("202"));
        Assert.False(
            createResponses.TryGetProperty("200", out _));
        Assert.False(
            createResponses.TryGetProperty("201", out _));

        JsonElement resourcePath =
            paths.GetProperty(
                "/api/simulation-jobs/{id}");
        Assert.Equal(
            ["get"],
            resourcePath.EnumerateObject()
                .Select(property => property.Name)
                .ToArray());

        JsonElement getOperation =
            resourcePath.GetProperty("get");
        JsonElement idParameter = Assert.Single(
            getOperation.GetProperty("parameters")
                .EnumerateArray());
        Assert.Equal(
            "id",
            idParameter.GetProperty("name").GetString());
        Assert.Equal(
            "path",
            idParameter.GetProperty("in").GetString());
        Assert.True(
            idParameter.GetProperty("required").GetBoolean());

        JsonElement idSchema =
            idParameter.GetProperty("schema");
        Assert.Equal(
            "string",
            idSchema.GetProperty("type").GetString());
        Assert.Equal(
            "uuid",
            idSchema.GetProperty("format").GetString());

        JsonElement getResponses =
            getOperation.GetProperty("responses");
        Assert.Equal(
            ["200", "404"],
            getResponses.EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray());
        AssertResponseSchema(
            root,
            getResponses.GetProperty("200"));
        Assert.False(
            getResponses.TryGetProperty("202", out _));
    }

    private static void AssertResponseSchema(
        JsonElement root,
        JsonElement response)
    {
        JsonElement schema = ResolveSchema(
            root,
            response
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));

        AssertSchemaProperties(
            root,
            schema,
            new Dictionary<string, string>
            {
                ["id"] = "string",
                ["status"] = "string",
                ["request"] = "object",
                ["createdAtUtc"] = "string",
                ["claimedAtUtc"] = "string",
                ["finishedAtUtc"] = "string",
                ["simulationId"] = "string",
                ["failureMessage"] = "string"
            });

        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal(
            "uuid",
            properties.GetProperty("id")
                .GetProperty("format")
                .GetString());
        Assert.Equal(
            "date-time",
            properties.GetProperty("createdAtUtc")
                .GetProperty("format")
                .GetString());

        AssertNullableSchema(
            properties.GetProperty("claimedAtUtc"));
        AssertNullableSchema(
            properties.GetProperty("finishedAtUtc"));
        AssertNullableSchema(
            properties.GetProperty("simulationId"));
        AssertNullableSchema(
            properties.GetProperty("failureMessage"));

        JsonElement nestedRequest = ResolveSchema(
            root,
            properties.GetProperty("request"));
        AssertSchemaProperties(
            root,
            nestedRequest,
            new Dictionary<string, string>
            {
                ["singularTime"] = "number",
                ["concentrationExponent"] = "number",
                ["startTime"] = "number",
                ["endTime"] = "number",
                ["sampleCount"] = "integer",
                ["samplingMode"] = "string"
            });
    }

    private static void AssertSchemaProperties(
        JsonElement root,
        JsonElement schema,
        IReadOnlyDictionary<string, string> expected)
    {
        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal(
            expected.Keys.Order(),
            properties.EnumerateObject()
                .Select(property => property.Name)
                .Order());

        foreach ((string propertyName, string expectedType) in expected)
        {
            AssertSchemaIncludesType(
                ResolveSchema(
                    root,
                    properties.GetProperty(propertyName)),
                expectedType);
        }
    }

    private static void AssertNullableSchema(JsonElement schema)
    {
        JsonElement type = schema.GetProperty("type");
        Assert.Equal(JsonValueKind.Array, type.ValueKind);
        Assert.Contains(
            "null",
            type.EnumerateArray()
                .Select(element => element.GetString()));
    }

    private static void AssertSchemaIncludesType(
        JsonElement schema,
        string expectedType)
    {
        JsonElement type = schema.GetProperty("type");
        if (type.ValueKind == JsonValueKind.String)
        {
            Assert.Equal(expectedType, type.GetString());
            return;
        }

        Assert.Contains(
            expectedType,
            type.EnumerateArray()
                .Select(element => element.GetString()));
    }

    private static JsonElement ResolveSchema(
        JsonElement root,
        JsonElement schema)
    {
        if (!schema.TryGetProperty(
                "$ref",
                out JsonElement reference))
        {
            return schema;
        }

        const string Prefix = "#/components/schemas/";
        string referenceValue =
            reference.GetString()
            ?? throw new InvalidOperationException(
                "OpenAPI schema reference cannot be null.");

        Assert.StartsWith(Prefix, referenceValue);

        return root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(referenceValue[Prefix.Length..]);
    }
}