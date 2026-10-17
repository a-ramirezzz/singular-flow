using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.SimulationJobs;

public interface ISimulationJobCompletionRepository
{
    /// <summary>
    /// Atomically persists the simulation and transitions the Running job to
    /// Completed. A null result means no simulation data was committed.
    /// </summary>
    Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
        Guid jobId,
        RunSimulationResult simulation,
        CancellationToken cancellationToken);
}