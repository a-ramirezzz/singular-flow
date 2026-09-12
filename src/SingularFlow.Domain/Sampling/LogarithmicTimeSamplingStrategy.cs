using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Sampling;

public sealed class LogarithmicTimeSamplingStrategy
    : ITimeSamplingStrategy
{
    public IReadOnlyList<double> GenerateTimes(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        TimeSamplingValidation.Validate(
            blowupParameters,
            seriesParameters);

        double initialRemainingTime =
            blowupParameters.SingularTime -
            seriesParameters.StartTime;

        double finalRemainingTime =
            blowupParameters.SingularTime -
            seriesParameters.EndTime;

        double geometricRatio =
            CalculateGeometricRatio(
                initialRemainingTime,
                finalRemainingTime,
                seriesParameters.SampleCount);

        double[] times =
            new double[seriesParameters.SampleCount];

        for (
            int index = 0;
            index < seriesParameters.SampleCount;
            index++)
        {
            times[index] = CalculateSampleTime(
                index,
                initialRemainingTime,
                geometricRatio,
                blowupParameters,
                seriesParameters);
        }

        return Array.AsReadOnly(times);
    }

    private static double CalculateGeometricRatio(
        double initialRemainingTime,
        double finalRemainingTime,
        int sampleCount)
    {
        double remainingTimeRatio =
            finalRemainingTime /
            initialRemainingTime;

        double exponent =
            1.0 / (sampleCount - 1);

        return Math.Pow(
            remainingTimeRatio,
            exponent);
    }

    private static double CalculateSampleTime(
        int index,
        double initialRemainingTime,
        double geometricRatio,
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        if (index == 0)
        {
            return seriesParameters.StartTime;
        }

        bool isLastSample =
            index == seriesParameters.SampleCount - 1;

        if (isLastSample)
        {
            return seriesParameters.EndTime;
        }

        double remainingTime =
            initialRemainingTime *
            Math.Pow(geometricRatio, index);

        return blowupParameters.SingularTime -
               remainingTime;
    }
}