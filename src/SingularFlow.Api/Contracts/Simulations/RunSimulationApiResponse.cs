namespace SingularFlow.Api.Contracts.Simulations;

public sealed record RunSimulationApiResponse(
    double SingularTime,
    double ConcentrationExponent,
    double StartTime,
    double EndTime,
    int SampleCount,
    string SamplingMode,
    IReadOnlyList<SimulationStateResponse> States);