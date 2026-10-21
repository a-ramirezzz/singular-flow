namespace SingularFlow.Application.SimulationJobs;

public sealed class RecoverExpiredSimulationJobsHandler
{
    private readonly ISimulationJobLeaseRepository _repository;

    public RecoverExpiredSimulationJobsHandler(
        ISimulationJobLeaseRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<Guid>> HandleAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        return _repository.RecoverExpiredAsync(
            batchSize,
            cancellationToken);
    }
}