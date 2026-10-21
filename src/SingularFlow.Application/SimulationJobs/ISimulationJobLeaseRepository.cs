namespace SingularFlow.Application.SimulationJobs;

public interface ISimulationJobLeaseRepository
{
    Task<SimulationJobClaim?> ClaimNextPendingAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<DateTimeOffset?> TryRenewLeaseAsync(
        Guid jobId,
        Guid claimToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> RecoverExpiredAsync(
        int batchSize,
        CancellationToken cancellationToken);
}