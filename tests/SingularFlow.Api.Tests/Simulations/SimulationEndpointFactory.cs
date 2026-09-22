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
        public Task SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}