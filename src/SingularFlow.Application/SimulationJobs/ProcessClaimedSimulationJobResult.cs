namespace SingularFlow.Application.SimulationJobs;

public abstract record ProcessClaimedSimulationJobResult
{
    private ProcessClaimedSimulationJobResult()
    {
    }

    public sealed record Completed(
        Guid JobId,
        Guid SimulationId) : ProcessClaimedSimulationJobResult;

    public sealed record LeaseLost(
        Guid JobId) : ProcessClaimedSimulationJobResult;
}