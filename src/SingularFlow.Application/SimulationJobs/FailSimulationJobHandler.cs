namespace SingularFlow.Application.SimulationJobs;

public sealed class FailSimulationJobHandler
{
    private readonly ISimulationJobRepository _repository;

    public FailSimulationJobHandler(
        ISimulationJobRepository repository)
    {
        _repository = repository;
    }

    public Task<SimulationJobResult?> HandleAsync(
        Guid jobId,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            failureMessage);

        if (failureMessage.Length >
            SimulationJobResult.MaximumFailureMessageLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureMessage),
                $"Failure message must not exceed " +
                $"{SimulationJobResult.MaximumFailureMessageLength} " +
                "characters.");
        }

        return _repository.TryFailAsync(
            jobId,
            failureMessage,
            cancellationToken);
    }
}