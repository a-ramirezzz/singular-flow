using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Contracts.Simulations;

internal static class SimulationContractMapper
{
    public static RunSimulationRequest ToApplication(
        RunSimulationApiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RunSimulationRequest(
            SingularTime: request.SingularTime,
            ConcentrationExponent:
                request.ConcentrationExponent,
            StartTime: request.StartTime,
            EndTime: request.EndTime,
            SampleCount: request.SampleCount,
            SamplingMode: ParseSamplingMode(
                request.SamplingMode));
    }

    public static RunSimulationApiResponse ToApi(
        PersistedSimulationResult persisted)
    {
        ArgumentNullException.ThrowIfNull(persisted);

        RunSimulationResult result =
            persisted.Simulation;

        SimulationStateResponse[] states = result.States
            .Select(state => new SimulationStateResponse(
                Time: state.Time,
                RemainingTime: state.RemainingTime,
                RadialLength: state.RadialLength,
                AxialLength: state.AxialLength,
                AngularVelocityScale:
                    state.AngularVelocityScale,
                RadialVelocityScale:
                    state.RadialVelocityScale,
                CoreVolumeScale:
                    state.CoreVolumeScale,
                CoreEnergyScale:
                    state.CoreEnergyScale))
            .ToArray();

        return new RunSimulationApiResponse(
            Id: persisted.Id,
            CreatedAtUtc: persisted.CreatedAtUtc,
            SingularTime:
                result.BlowupParameters.SingularTime,
            ConcentrationExponent:
                result.BlowupParameters
                    .ConcentrationExponent,
            StartTime:
                result.TimeSeriesParameters.StartTime,
            EndTime:
                result.TimeSeriesParameters.EndTime,
            SampleCount:
                result.TimeSeriesParameters.SampleCount,
            SamplingMode:
                ToApiValue(result.SamplingMode),
            States: states);
    }

    private static SamplingMode ParseSamplingMode(
        string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "uniform" => SamplingMode.Uniform,
            "logarithmic" => SamplingMode.Logarithmic,

            _ => throw new ArgumentOutOfRangeException(
                "samplingMode",
                value,
                "Sampling mode must be either " +
                "'uniform' or 'logarithmic'.")
        };
    }

    private static string ToApiValue(
        SamplingMode samplingMode)
    {
        return samplingMode switch
        {
            SamplingMode.Uniform => "uniform",
            SamplingMode.Logarithmic => "logarithmic",

            _ => throw new ArgumentOutOfRangeException(
                nameof(samplingMode),
                samplingMode,
                "Sampling mode is not supported.")
        };
    }
}