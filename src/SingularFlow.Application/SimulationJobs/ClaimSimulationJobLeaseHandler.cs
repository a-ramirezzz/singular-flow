namespace SingularFlow.Application.SimulationJobs;

public sealed class ClaimSimulationJobLeaseHandler
{
    private readonly ISimulationJobLeaseRepository _repository;

    public ClaimSimulationJobLeaseHandler(
        ISimulationJobLeaseRepository repository)
    {
        _repository = repository;
    }

    public Task<SimulationJobClaim?> HandleAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        return _repository.ClaimNextPendingAsync(
            leaseDuration,
            cancellationToken);
    }
}