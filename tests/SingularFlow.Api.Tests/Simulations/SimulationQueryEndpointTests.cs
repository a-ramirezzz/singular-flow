using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationQueryEndpointTests
{
    [Fact]
    public async Task GetSimulation_ExistingId_ReturnsPersistedSimulation()
    {
        PersistedSimulationResult persisted =
            CreatePersistedSimulation();

        QuerySimulationRepository repository = new(
            persisted);

        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<
                            ISimulationRepository>();

                        services.AddSingleton<
                            ISimulationRepository>(
                                repository);
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
                $"/api/simulations/{persisted.Id}");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                contentStream);

        JsonElement root = document.RootElement;

        Assert.Equal(
            persisted.Id,
            root.GetProperty("id").GetGuid());

        Assert.Equal(
            persisted.CreatedAtUtc,
            root.GetProperty("createdAtUtc")
                .GetDateTimeOffset());

        Assert.Equal(
            3,
            root.GetProperty("states")
                .GetArrayLength());
    }

    [Fact]
    public async Task GetSimulation_MissingId_ReturnsNotFound()
    {
        PersistedSimulationResult persisted =
            CreatePersistedSimulation();

        QuerySimulationRepository repository = new(
            persisted);

        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<
                            ISimulationRepository>();

                        services.AddSingleton<
                            ISimulationRepository>(
                                repository);
                    });
                });

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri(
                "https://localhost")
        };

        using HttpClient client =
            factory.CreateClient(options);

        Guid missingId = Guid.NewGuid();

        using HttpResponseMessage response =
            await client.GetAsync(
                $"/api/simulations/{missingId}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    private static PersistedSimulationResult
        CreatePersistedSimulation()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        RunSimulationResult simulation =
            new RunSimulationHandler().Handle(
                request);

        return new PersistedSimulationResult(
            Id: Guid.Parse(
                "89e58894-f2ab-4528-8030-75c727887611"),
            CreatedAtUtc: new DateTimeOffset(
                2026,
                9,
                23,
                12,
                0,
                0,
                TimeSpan.Zero),
            Simulation: simulation);
    }

    private sealed class QuerySimulationRepository :
        ISimulationRepository
    {
        public Task<PagedSimulationResult> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
        private readonly PersistedSimulationResult _persisted;

        public QuerySimulationRepository(
            PersistedSimulationResult persisted)
        {
            _persisted = persisted;
        }

        public Task<PersistedSimulationResult> SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PersistedSimulationResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            PersistedSimulationResult? result =
                id == _persisted.Id
                    ? _persisted
                    : null;

            return Task.FromResult(result);
        }
    }
}