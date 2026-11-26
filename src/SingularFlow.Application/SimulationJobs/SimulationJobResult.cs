using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.SimulationJobs;

public sealed record SimulationJobResult(
    Guid Id,
    RunSimulationRequest Request,
    SimulationJobStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    Guid? SimulationId,
    DateTimeOffset? FinishedAtUtc,
    string? FailureMessage)
{
    public const int MaximumFailureMessageLength = 1000;

    public int AttemptCount { get; init; }

    public int MaxAttempts { get; init; } = 3;

    public DateTimeOffset? NextAttemptAtUtc { get; init; }

    public DateTimeOffset? LastFailedAtUtc { get; init; }

    public string? LastFailureMessage { get; init; }
}