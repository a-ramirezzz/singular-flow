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

    Task<SimulationJobResult?> TryCompleteAsync(
        Guid jobId,
        Guid claimToken,
        Guid simulationId,
        CancellationToken cancellationToken);

    Task<SimulationJobResult?> TryFailAsync(
        Guid jobId,
        Guid claimToken,
        string failureMessage,
        CancellationToken cancellationToken);
}