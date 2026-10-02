using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.SimulationJobs;

public interface ISimulationJobRepository
{
    Task<SimulationJobResult> CreateAsync(
        RunSimulationRequest request,
        CancellationToken cancellationToken);

    Task<SimulationJobResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);
}