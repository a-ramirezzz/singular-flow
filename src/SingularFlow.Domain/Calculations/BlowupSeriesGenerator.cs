using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Calculations;

public sealed class BlowupSeriesGenerator
{
    private readonly BlowupScalingCalculator _calculator;

    public BlowupSeriesGenerator(
        BlowupScalingCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);

        _calculator = calculator;
    }

    public IReadOnlyList<BlowupState> Generate(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        ArgumentNullException.ThrowIfNull(blowupParameters);
        ArgumentNullException.ThrowIfNull(seriesParameters);

        ValidateSeriesRange(
            blowupParameters,
            seriesParameters);

        double timeStep =
            CalculateTimeStep(seriesParameters);

        List<BlowupState> states = new(
            capacity: seriesParameters.SampleCount);

        for (
            int index = 0;
            index < seriesParameters.SampleCount;
            index++)
        {
            double time = CalculateSampleTime(
                index,
                timeStep,
                seriesParameters);

            BlowupState state = _calculator.Calculate(
                time,
                blowupParameters);

            states.Add(state);
        }

        return states.AsReadOnly();
    }

    private static void ValidateSeriesRange(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        if (seriesParameters.EndTime >=
            blowupParameters.SingularTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seriesParameters),
                "Series end time must be less than " +
                "the singular time.");
        }
    }

    private static double CalculateTimeStep(
        TimeSeriesParameters seriesParameters)
    {
        return
            (seriesParameters.EndTime -
             seriesParameters.StartTime) /
            (seriesParameters.SampleCount - 1);
    }

    private static double CalculateSampleTime(
        int index,
        double timeStep,
        TimeSeriesParameters seriesParameters)
    {
        bool isLastSample =
            index == seriesParameters.SampleCount - 1;

        if (isLastSample)
        {
            return seriesParameters.EndTime;
        }

        return seriesParameters.StartTime +
               (index * timeStep);
    }
}