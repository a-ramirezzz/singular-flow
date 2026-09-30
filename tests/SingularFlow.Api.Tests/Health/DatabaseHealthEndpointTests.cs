using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using Npgsql;

namespace SingularFlow.Api.Tests.Health;

public sealed class DatabaseHealthEndpointTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task GetReadiness_WithAvailablePostgreSql_ReturnsHealthyResponse(
        string endpoint)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(
                "SINGULARFLOW_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");

        NpgsqlConnectionStringBuilder parsedConnection =
            new(connectionString);

        Assert.Equal(
            "singular_flow_tests",
            parsedConnection.Database);

        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration(
                        (_, configuration) =>
                        {
                            configuration.AddInMemoryCollection(
                                new Dictionary<string, string?>
                                {
                                    ["ConnectionStrings:SingularFlow"] =
                                        connectionString
                                });
                        });
                });

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri(
                "https://localhost")
        };

        using HttpClient client =
            factory.CreateClient(options);

        using HttpResponseMessage response =
            await client.GetAsync(
                endpoint);

        string content =
            await response.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            "Healthy",
            content);
    }
    [Fact]
    public async Task GetHealth_WithUnavailablePostgreSql_SeparatesLivenessAndReadiness()
    {
        string connectionString =
            Environment.GetEnvironmentVariable(
                "SINGULARFLOW_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");

        NpgsqlConnectionStringBuilder unavailableConnection =
            new(connectionString)
            {
                Host = "127.0.0.1",
                Port = 1,
                Timeout = 1,
                CommandTimeout = 1,
                Pooling = false
            };

        Assert.Equal(
            "singular_flow_tests",
            unavailableConnection.Database);

        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration(
                        (_, configuration) =>
                        {
                            configuration.AddInMemoryCollection(
                                new Dictionary<string, string?>
                                {
                                    ["ConnectionStrings:SingularFlow"] =
                                        unavailableConnection
                                            .ConnectionString
                                });
                        });
                });

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri(
                "https://localhost")
        };

        using HttpClient client =
            factory.CreateClient(options);

        using HttpResponseMessage livenessResponse =
            await client.GetAsync(
                "/health/live");

        string livenessContent =
            await livenessResponse.Content
                .ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            livenessResponse.StatusCode);

        Assert.Equal(
            "Healthy",
            livenessContent);

        using HttpResponseMessage readinessResponse =
            await client.GetAsync(
                "/health/ready");

        string readinessContent =
            await readinessResponse.Content
                .ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            readinessResponse.StatusCode);

        Assert.Equal(
            "Unhealthy",
            readinessContent);

        using HttpResponseMessage compatibilityResponse =
            await client.GetAsync(
                "/health");

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            compatibilityResponse.StatusCode);
    }
}