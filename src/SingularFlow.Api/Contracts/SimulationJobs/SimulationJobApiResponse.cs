using SingularFlow.Api.Contracts.Simulations;

namespace SingularFlow.Api.Contracts.SimulationJobs;

public sealed record SimulationJobApiResponse(
    Guid Id,
    string Status,
    RunSimulationApiRequest Request,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    Guid? SimulationId,
    string? FailureMessage);