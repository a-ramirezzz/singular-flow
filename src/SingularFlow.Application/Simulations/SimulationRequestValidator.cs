using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

namespace SingularFlow.Application.Simulations;

internal static class SimulationRequestValidator
{
    public static ValidatedSimulationRequest Validate(
        RunSimulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ITimeSamplingStrategy samplingStrategy =
            CreateSamplingStrategy(request.SamplingMode);

        BlowupParameters blowupParameters = new(
            singularTime: request.SingularTime,
            concentrationExponent:
                request.ConcentrationExponent);

        TimeSeriesParameters timeSeriesParameters = new(
            startTime: request.StartTime,
            endTime: request.EndTime,
            sampleCount: request.SampleCount);

        ValidateSeriesEnd(
            blowupParameters,
            timeSeriesParameters);

        return new ValidatedSimulationRequest(
            blowupParameters,
            timeSeriesParameters,
            samplingStrategy);
    }

    private static ITimeSamplingStrategy CreateSamplingStrategy(
        SamplingMode samplingMode)
    {
        return samplingMode switch
        {
            SamplingMode.Uniform =>
                new UniformTimeSamplingStrategy(),

            SamplingMode.Logarithmic =>
                new LogarithmicTimeSamplingStrategy(),

            _ => throw new ArgumentOutOfRangeException(
                nameof(samplingMode),
                samplingMode,
                "Sampling mode is not supported.")
        };
    }

    private static void ValidateSeriesEnd(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        if (seriesParameters.EndTime >=
            blowupParameters.SingularTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seriesParameters),
                seriesParameters.EndTime,
                "Series end time must be less than " +
                "the singular time.");
        }
    }
}

internal sealed record ValidatedSimulationRequest(
    BlowupParameters BlowupParameters,
    TimeSeriesParameters TimeSeriesParameters,
    ITimeSamplingStrategy SamplingStrategy);