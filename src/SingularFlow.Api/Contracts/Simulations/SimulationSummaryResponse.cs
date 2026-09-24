namespace SingularFlow.Api.Contracts.Simulations;

public sealed record SimulationSummaryResponse(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    double SingularTime,
    double ConcentrationExponent,
    double StartTime,
    double EndTime,
    int SampleCount,
    string SamplingMode);