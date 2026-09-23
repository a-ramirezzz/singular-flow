namespace SingularFlow.Application.Simulations;

public sealed record PersistedSimulationResult(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    RunSimulationResult Simulation);