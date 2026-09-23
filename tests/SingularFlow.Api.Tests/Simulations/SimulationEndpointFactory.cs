using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationEndpointFactory :
    WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISimulationRepository>();

            services.AddScoped<
                ISimulationRepository,
                NoOpSimulationRepository>();
        });
    }

    private sealed class NoOpSimulationRepository :
        ISimulationRepository
    {
        public Task<PersistedSimulationResult> SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            PersistedSimulationResult persisted = new(
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
                Simulation: result);

            return Task.FromResult(persisted);
        }

        public Task<PersistedSimulationResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<
                PersistedSimulationResult?>(null);
        }
    }
}