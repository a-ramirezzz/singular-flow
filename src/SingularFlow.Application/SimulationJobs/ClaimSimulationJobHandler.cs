namespace SingularFlow.Application.SimulationJobs;

public sealed class ClaimSimulationJobHandler
{
    private readonly ISimulationJobRepository _repository;

    public ClaimSimulationJobHandler(
        ISimulationJobRepository repository)
    {
        _repository = repository;
    }

    public Task<SimulationJobResult?> HandleAsync(
        CancellationToken cancellationToken)
    {
        return _repository.ClaimNextPendingAsync(
            cancellationToken);
    }
}