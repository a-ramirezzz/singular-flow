namespace SingularFlow.Application.Simulations;

public sealed class DeleteSimulationHandler
{
    private readonly ISimulationRepository _repository;

    public DeleteSimulationHandler(
        ISimulationRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> HandleAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return _repository.DeleteAsync(
            id,
            cancellationToken);
    }
}