using System.Net;
using System.Net.Http.Json;

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
                HttpStatusCode.OK,
                response.StatusCode);

            SimulationEntity saved =
                await context.Simulations
                    .AsNoTracking()
                    .Include(
                        simulation =>
                            simulation.States)
                    .SingleAsync(
                        simulation =>
                            simulation.SingularTime ==
                            singularTime);

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