namespace SingularFlow.Application.SimulationJobs;

public sealed class GetSimulationJobHandler
{
    private readonly ISimulationJobRepository _repository;

    public GetSimulationJobHandler(
        ISimulationJobRepository repository)
    {
        _repository = repository;
    }

    public Task<SimulationJobResult?> HandleAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return _repository.GetByIdAsync(
            id,
            cancellationToken);
    }
}