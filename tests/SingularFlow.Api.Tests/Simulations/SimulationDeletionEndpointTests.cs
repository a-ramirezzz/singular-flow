using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationDeletionEndpointTests
{
    private static readonly Guid ExistingId =
        Guid.Parse(
            "89e58894-f2ab-4528-8030-75c727887611");

    [Fact]
    public async Task DeleteSimulation_ExistingId_ReturnsNoContent()
    {
        DeletionSimulationRepository repository = new(
            ExistingId);

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
            await client.DeleteAsync(
                $"/api/simulations/{ExistingId}");

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        Assert.Equal(
            ExistingId,
            repository.DeletedId);
    }

    [Fact]
    public async Task DeleteSimulation_MissingId_ReturnsNotFound()
    {
        DeletionSimulationRepository repository = new(
            ExistingId);

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
            await client.DeleteAsync(
                $"/api/simulations/{missingId}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        Assert.Equal(
            missingId,
            repository.DeletedId);
    }

    private sealed class DeletionSimulationRepository :
        ISimulationRepository
    {
        private readonly Guid _existingId;

        public DeletionSimulationRepository(
            Guid existingId)
        {
            _existingId = existingId;
        }

        public Guid? DeletedId { get; private set; }

        public Task<bool> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            DeletedId = id;

            return Task.FromResult(
                id == _existingId);
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
            throw new NotSupportedException();
        }

        public Task<PagedSimulationResult> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}