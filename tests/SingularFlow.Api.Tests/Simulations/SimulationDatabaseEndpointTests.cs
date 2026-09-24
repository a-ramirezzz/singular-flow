using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationDatabaseEndpointTests
{
    [Fact]
    public async Task PostSimulation_SavesStatesInPostgreSql()
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

        using IServiceScope scope =
            factory.Services.CreateScope();

        SingularFlowDbContext context =
            scope.ServiceProvider.GetRequiredService<
                SingularFlowDbContext>();

        await context.Database.MigrateAsync();

        double singularTime =
            1.0 + Random.Shared.NextDouble();

        try
        {
            WebApplicationFactoryClientOptions options = new()
            {
                BaseAddress = new Uri("https://localhost")
            };

            using HttpClient client =
                factory.CreateClient(options);

            var request = new
            {
                singularTime,
                concentrationExponent = 0.005,
                startTime = 0.0,
                endTime = 0.8,
                sampleCount = 4,
                samplingMode = "uniform"
            };

            using HttpResponseMessage response =
                await client.PostAsJsonAsync(
                    "/api/simulations",
                    request);

            Assert.Equal(
             HttpStatusCode.Created,
             response.StatusCode);

            Uri location =
    Assert.IsType<Uri>(
        response.Headers.Location);

            await using Stream postContent =
                await response.Content.ReadAsStreamAsync();

            using JsonDocument postDocument =
                await JsonDocument.ParseAsync(
                    postContent);

            Guid simulationId =
                postDocument.RootElement
                    .GetProperty("id")
                    .GetGuid();

            Assert.Equal(
                $"/api/simulations/{simulationId}",
                location.AbsolutePath);

            using HttpResponseMessage getResponse =
                await client.GetAsync(location);

            Assert.Equal(
                HttpStatusCode.OK,
                getResponse.StatusCode);

            await using Stream getContent =
                await getResponse.Content
                    .ReadAsStreamAsync();

            using JsonDocument getDocument =
                await JsonDocument.ParseAsync(
                    getContent);

            JsonElement retrieved =
                getDocument.RootElement;

            Assert.Equal(
                simulationId,
                retrieved.GetProperty("id").GetGuid());

            Assert.Equal(
                singularTime,
                retrieved.GetProperty(
                    "singularTime").GetDouble());

            Assert.Equal(
                4,
                retrieved.GetProperty(
                    "sampleCount").GetInt32());

            Assert.Equal(
                "uniform",
                retrieved.GetProperty(
                    "samplingMode").GetString());

            JsonElement retrievedStates =
                retrieved.GetProperty("states");

            Assert.Equal(
                4,
                retrievedStates.GetArrayLength());

            Assert.Equal(
                0.0,
                retrievedStates[0]
                    .GetProperty("time")
                    .GetDouble());

            Assert.Equal(
                0.8,
                retrievedStates[3]
                    .GetProperty("time")
                    .GetDouble(),
                precision: 10);

            using HttpResponseMessage listResponse =
    await client.GetAsync(
        "/api/simulations?page=1&pageSize=100");

            Assert.Equal(
                HttpStatusCode.OK,
                listResponse.StatusCode);

            await using Stream listContent =
                await listResponse.Content
                    .ReadAsStreamAsync();

            using JsonDocument listDocument =
                await JsonDocument.ParseAsync(
                    listContent);

            JsonElement listRoot =
                listDocument.RootElement;

            Assert.Equal(
                1,
                listRoot.GetProperty("page")
                    .GetInt32());

            Assert.Equal(
                100,
                listRoot.GetProperty("pageSize")
                    .GetInt32());

            Assert.True(
                listRoot.GetProperty("totalCount")
                    .GetInt32() >= 1);

            JsonElement listedSimulation =
                listRoot.GetProperty("items")
                    .EnumerateArray()
                    .Single(item =>
                        item.GetProperty("id")
                            .GetGuid() ==
                        simulationId);

            Assert.Equal(
                singularTime,
                listedSimulation
                    .GetProperty("singularTime")
                    .GetDouble());

            Assert.Equal(
                4,
                listedSimulation
                    .GetProperty("sampleCount")
                    .GetInt32());

            Assert.False(
                listedSimulation.TryGetProperty(
                    "states",
                    out _));

            SimulationEntity saved =
                await context.Simulations
                    .AsNoTracking()
                    .Include(
                        simulation =>
                            simulation.States)
                    .SingleAsync(
                        simulation =>
                            simulation.Id ==
                            simulationId);

            Assert.Equal(4, saved.SampleCount);
            Assert.NotEqual(default, saved.CreatedAtUtc);

            SimulationStateEntity[] states =
                saved.States
                    .OrderBy(state => state.Sequence)
                    .ToArray();

            Assert.Equal(4, states.Length);

            for (int index = 0; index < states.Length; index++)
            {
                Assert.Equal(index, states[index].Sequence);
            }

            Assert.Equal(0.0, states[0].Time);
            Assert.Equal(
                0.8,
                states[3].Time,
                precision: 10);
        }
        finally
        {
            await context.Simulations
                .Where(
                    simulation =>
                        simulation.SingularTime ==
                        singularTime)
                .ExecuteDeleteAsync();
        }
    }
}