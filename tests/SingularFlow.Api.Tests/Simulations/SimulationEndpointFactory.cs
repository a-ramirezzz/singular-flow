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
        public Task<bool> DeleteAsync(
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
            PagedSimulationResult result = new(
                Items: Array.Empty<SimulationSummaryResult>(),
                Page: page,
                PageSize: pageSize,
                TotalCount: 0);

            return Task.FromResult(result);
        }
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