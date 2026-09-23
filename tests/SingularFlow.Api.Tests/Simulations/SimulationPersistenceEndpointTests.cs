using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationPersistenceEndpointTests
{
    [Fact]

    public async Task PostSimulation_ValidRequest_SavesCalculatedStates()
    {
        RecordingSimulationRepository repository = new();

        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<ISimulationRepository>();

                        services.AddSingleton<ISimulationRepository>(
                            repository);
                    });
                });

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        using HttpClient client = factory.CreateClient(options);

        var request = new
        {
            singularTime = 1.0,
            concentrationExponent = 0.005,
            startTime = 0.0,
            endTime = 0.8,
            sampleCount = 3,
            samplingMode = "uniform"
        };

        using HttpResponseMessage response =
            await client.PostAsJsonAsync(
                "/api/simulations",
                request);

        Assert.Equal(
    HttpStatusCode.Created,
    response.StatusCode);

        Assert.Equal(
            $"/api/simulations/{PersistedId}",
            response.Headers.Location?.AbsolutePath);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(contentStream);

        JsonElement root = document.RootElement;

        Assert.Equal(
            PersistedId,
            root.GetProperty("id").GetGuid());

        Assert.Equal(
            PersistedCreatedAtUtc,
            root.GetProperty("createdAtUtc")
                .GetDateTimeOffset());

        RunSimulationResult saved =
            Assert.IsType<RunSimulationResult>(
                repository.SavedResult);

        Assert.Equal(3, saved.States.Count);
        Assert.Equal(0.0, saved.States[0].Time);
        Assert.Equal(
            0.8,
            saved.States[2].Time,
            precision: 10);
    }

    private static readonly Guid PersistedId =
        Guid.Parse(
            "89e58894-f2ab-4528-8030-75c727887611");

    private static readonly DateTimeOffset PersistedCreatedAtUtc =
        new(
            2026,
            9,
            23,
            12,
            0,
            0,
            TimeSpan.Zero);

    private sealed class RecordingSimulationRepository :
        ISimulationRepository
    {
        public RunSimulationResult? SavedResult { get; private set; }

        public Task<PersistedSimulationResult> SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            SavedResult = result;

            PersistedSimulationResult persisted = new(
                Id: PersistedId,
                CreatedAtUtc: PersistedCreatedAtUtc,
                Simulation: result);

            return Task.FromResult(persisted);
        }

        public Task<PersistedSimulationResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}