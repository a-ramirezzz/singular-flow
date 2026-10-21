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
    }
}