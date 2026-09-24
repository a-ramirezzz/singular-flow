namespace SingularFlow.Application.Simulations;

public interface ISimulationRepository
{
    Task<PersistedSimulationResult> SaveAsync(
        RunSimulationResult result,
        CancellationToken cancellationToken);

    Task<PersistedSimulationResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<PagedSimulationResult> ListAsync(
    int page,
    int pageSize,
        CancellationToken cancellationToken);
}