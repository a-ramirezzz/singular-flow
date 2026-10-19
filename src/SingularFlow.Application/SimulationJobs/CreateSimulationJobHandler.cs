using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.SimulationJobs;

public sealed class CreateSimulationJobHandler
{
    private readonly ISimulationJobRepository _repository;

    public CreateSimulationJobHandler(
        ISimulationJobRepository repository)
    {
        _repository = repository;
    }

    public Task<SimulationJobResult> HandleAsync(
        RunSimulationRequest request,
        CancellationToken cancellationToken)
    {
        SimulationRequestValidator.Validate(request);

        return _repository.CreateAsync(
            request,
            cancellationToken);
    }
}