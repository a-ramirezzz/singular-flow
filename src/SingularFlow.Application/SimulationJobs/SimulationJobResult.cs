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
}