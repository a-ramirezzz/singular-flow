using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.SimulationJobs;

public sealed class CreateSimulationJobHandler
{
    private readonly ISimulationJobRepository _repository;
    private readonly SimulationJobRetryPolicy _retryPolicy;

    public CreateSimulationJobHandler(
        ISimulationJobRepository repository,
        SimulationJobRetryPolicy retryPolicy)
    {
        _repository = repository ??
            throw new ArgumentNullException(nameof(repository));
        _retryPolicy = retryPolicy ??
            throw new ArgumentNullException(nameof(retryPolicy));
    }

    public Task<SimulationJobResult> HandleAsync(
        RunSimulationRequest request,
        CancellationToken cancellationToken)
    {
        SimulationRequestValidator.Validate(request);

        return _repository.CreateAsync(
            request,
            _retryPolicy.MaxAttempts,
            cancellationToken);
    }
}