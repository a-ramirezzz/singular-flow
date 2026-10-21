namespace SingularFlow.Application.SimulationJobs;

public sealed class RenewSimulationJobLeaseHandler
{
    private readonly ISimulationJobLeaseRepository _repository;

    public RenewSimulationJobLeaseHandler(
        ISimulationJobLeaseRepository repository)
    {
        _repository = repository;
    }

    public Task<DateTimeOffset?> HandleAsync(
        Guid jobId,
        Guid claimToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        return _repository.TryRenewLeaseAsync(
            jobId,
            claimToken,
            leaseDuration,
            cancellationToken);
    }
}