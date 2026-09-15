using SingularFlow.Domain.Models;

namespace SingularFlow.Application.Simulations;

public sealed record RunSimulationResult(
    BlowupParameters BlowupParameters,
    TimeSeriesParameters TimeSeriesParameters,
    SamplingMode SamplingMode,
    IReadOnlyList<BlowupState> States);