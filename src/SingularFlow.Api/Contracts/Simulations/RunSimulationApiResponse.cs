namespace SingularFlow.Api.Contracts.Simulations;

public sealed record RunSimulationApiResponse(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    double SingularTime,
    double ConcentrationExponent,
    double StartTime,
    double EndTime,
    int SampleCount,
    string SamplingMode,
    IReadOnlyList<SimulationStateResponse> States);