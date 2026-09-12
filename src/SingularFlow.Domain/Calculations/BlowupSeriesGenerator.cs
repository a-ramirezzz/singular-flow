using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

namespace SingularFlow.Domain.Calculations;

public sealed class BlowupSeriesGenerator
{
    private readonly BlowupScalingCalculator _calculator;

    private readonly ITimeSamplingStrategy _samplingStrategy;

    public BlowupSeriesGenerator(
        BlowupScalingCalculator calculator,
        ITimeSamplingStrategy samplingStrategy)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(samplingStrategy);

        _calculator = calculator;
        _samplingStrategy = samplingStrategy;
    }

    public IReadOnlyList<BlowupState> Generate(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        ArgumentNullException.ThrowIfNull(
            blowupParameters);

        ArgumentNullException.ThrowIfNull(
            seriesParameters);

        IReadOnlyList<double> times =
            _samplingStrategy.GenerateTimes(
                blowupParameters,
                seriesParameters);

        List<BlowupState> states = new(
            capacity: times.Count);

        foreach (double time in times)
        {
            BlowupState state = _calculator.Calculate(
                time,
                blowupParameters);

            states.Add(state);
        }

        return states.AsReadOnly();
    }
}