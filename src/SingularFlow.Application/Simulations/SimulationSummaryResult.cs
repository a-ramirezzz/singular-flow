namespace SingularFlow.Application.Simulations;

public sealed record SimulationSummaryResult(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    double SingularTime,
    double ConcentrationExponent,
    double StartTime,
    double EndTime,
    int SampleCount,
    SamplingMode SamplingMode);