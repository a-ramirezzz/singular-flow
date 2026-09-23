namespace SingularFlow.Application.Simulations;

public sealed class RunAndSaveSimulationHandler
{
    private readonly RunSimulationHandler _simulationHandler;
    private readonly ISimulationRepository _repository;

    public RunAndSaveSimulationHandler(
        RunSimulationHandler simulationHandler,
        ISimulationRepository repository)
    {
        _simulationHandler = simulationHandler;
        _repository = repository;
    }

    public async Task<PersistedSimulationResult> HandleAsync(
    RunSimulationRequest request,
    CancellationToken cancellationToken)
    {
        RunSimulationResult result =
            _simulationHandler.Handle(request);

        PersistedSimulationResult persisted =
            await _repository.SaveAsync(
                result,
                cancellationToken);

        return persisted;
    }
}