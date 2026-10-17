namespace SingularFlow.Application.SimulationJobs;

public abstract record ProcessNextSimulationJobResult
{
    private ProcessNextSimulationJobResult()
    {
    }

    public sealed record NoWork : ProcessNextSimulationJobResult;

    public sealed record Completed(
        Guid JobId,
        Guid SimulationId) : ProcessNextSimulationJobResult;
}