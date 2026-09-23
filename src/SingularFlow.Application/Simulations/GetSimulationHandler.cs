namespace SingularFlow.Application.Simulations;

public sealed class GetSimulationHandler
{
    private readonly ISimulationRepository _repository;

    public GetSimulationHandler(
        ISimulationRepository repository)
    {
        _repository = repository;
    }

    public Task<PersistedSimulationResult?> HandleAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return _repository.GetByIdAsync(
            id,
            cancellationToken);
    }
}