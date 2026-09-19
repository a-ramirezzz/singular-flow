namespace SingularFlow.Api.Contracts.Simulations;

public sealed record RunSimulationApiRequest(
    double SingularTime,
    double ConcentrationExponent,
    double StartTime,
    double EndTime,
    int SampleCount,
    string SamplingMode);