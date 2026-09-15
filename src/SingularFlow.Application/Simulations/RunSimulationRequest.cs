namespace SingularFlow.Application.Simulations;

public sealed record RunSimulationRequest(
    double SingularTime,
    double ConcentrationExponent,
    double StartTime,
    double EndTime,
    int SampleCount,
    SamplingMode SamplingMode);