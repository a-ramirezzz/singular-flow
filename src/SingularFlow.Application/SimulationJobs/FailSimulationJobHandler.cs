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
        Guid claimToken,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        ValidateFailureMessage(failureMessage);

        return _repository.TryFailAsync(
            jobId,
            claimToken,
            failureMessage,
            cancellationToken);
    }

    private static void ValidateFailureMessage(
        string failureMessage)
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
    }
}