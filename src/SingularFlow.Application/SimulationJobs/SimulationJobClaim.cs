namespace SingularFlow.Application.SimulationJobs;

public sealed record SimulationJobClaim(
    SimulationJobResult Job,
    Guid ClaimToken,
    DateTimeOffset LeaseExpiresAtUtc)
{
    public void Validate()
    {
        if (Job is null)
        {
            throw new InvalidOperationException(
                "Simulation job claim has no job snapshot.");
        }

        if (Job.Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has an empty ID.");
        }

        if (Job.Status != SimulationJobStatus.Running)
        {
            throw new InvalidOperationException(
                "Claimed simulation job is not Running.");
        }

        if (Job.ClaimedAtUtc is null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has no claim timestamp.");
        }

        if (ClaimToken == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Simulation job claim has an empty claim token.");
        }

        if (LeaseExpiresAtUtc <= Job.ClaimedAtUtc.Value)
        {
            throw new InvalidOperationException(
                "Simulation job claim has an invalid lease interval.");
        }

        if (Job.SimulationId is not null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job already has a simulation ID.");
        }

        if (Job.FinishedAtUtc is not null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job already has a finish timestamp.");
        }

        if (Job.FailureMessage is not null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job already has a failure message.");
        }

        if (Job.Request is null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has no simulation request.");
        }

        if (Job.AttemptCount < 1)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has an invalid attempt count.");
        }

        if (Job.MaxAttempts < SimulationJobRetryPolicy.MinimumAttempts)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has too few maximum attempts.");
        }

        if (Job.MaxAttempts >
            SimulationJobRetryPolicy.MaximumAttemptsLimit)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has too many maximum attempts.");
        }

        if (Job.AttemptCount > Job.MaxAttempts)
        {
            throw new InvalidOperationException(
                "Claimed simulation job attempt exceeds its maximum attempts.");
        }

        if (Job.NextAttemptAtUtc is not null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job still has a next-attempt timestamp.");
        }

        if ((Job.LastFailedAtUtc is null) !=
            (Job.LastFailureMessage is null))
        {
            throw new InvalidOperationException(
                "Claimed simulation job has incomplete previous-failure metadata.");
        }

        if (Job.LastFailureMessage is not null &&
            string.IsNullOrWhiteSpace(Job.LastFailureMessage))
        {
            throw new InvalidOperationException(
                "Claimed simulation job has an invalid previous-failure message.");
        }

        if (Job.LastFailedAtUtc > Job.ClaimedAtUtc)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has a previous failure after its claim timestamp.");
        }
    }
}