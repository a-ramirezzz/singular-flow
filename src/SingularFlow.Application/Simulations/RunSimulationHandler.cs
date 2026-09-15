using SingularFlow.Domain.Calculations;
using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

namespace SingularFlow.Application.Simulations;

public sealed class RunSimulationHandler
{
    public RunSimulationResult Handle(
        RunSimulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ITimeSamplingStrategy samplingStrategy =
            CreateSamplingStrategy(
                request.SamplingMode);

        BlowupParameters blowupParameters = new(
            singularTime: request.SingularTime,
            concentrationExponent:
                request.ConcentrationExponent);

        TimeSeriesParameters timeSeriesParameters = new(
            startTime: request.StartTime,
            endTime: request.EndTime,
            sampleCount: request.SampleCount);

        BlowupScalingCalculator calculator = new();

        BlowupSeriesGenerator seriesGenerator = new(
            calculator,
            samplingStrategy);

        IReadOnlyList<BlowupState> states =
            seriesGenerator.Generate(
                blowupParameters,
                timeSeriesParameters);

        return new RunSimulationResult(
            BlowupParameters: blowupParameters,
            TimeSeriesParameters: timeSeriesParameters,
            SamplingMode: request.SamplingMode,
            States: states);
    }

    private static ITimeSamplingStrategy
        CreateSamplingStrategy(
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
}