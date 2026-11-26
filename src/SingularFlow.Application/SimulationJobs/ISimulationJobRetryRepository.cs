namespace SingularFlow.Application.SimulationJobs;

public interface ISimulationJobRetryRepository
{
    /// <summary>
    /// Records a retryable failure only for the exact job and current claim
    /// token. The failure message must already be safe for persistence.
    /// A scheduled decision means the job became Pending, an exhausted
    /// decision means it became terminal Failed, and null means the current
    /// ownership, status, or lease rejected the transition.
    /// </summary>
    Task<SimulationJobRetryDecision?> TryRecordRetryableFailureAsync(
        Guid jobId,
        Guid claimToken,
        string safeFailureMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);
}