namespace SingularFlow.Application.SimulationJobs;

public sealed class CompleteSimulationJobHandler
{
    private readonly ISimulationJobRepository _repository;

    public CompleteSimulationJobHandler(
        ISimulationJobRepository repository)
    {
        _repository = repository;
    }

    public Task<SimulationJobResult?> HandleAsync(
        Guid jobId,
        Guid simulationId,
        CancellationToken cancellationToken)
    {
        return _repository.TryCompleteAsync(
            jobId,
            simulationId,
            cancellationToken);
    }
}