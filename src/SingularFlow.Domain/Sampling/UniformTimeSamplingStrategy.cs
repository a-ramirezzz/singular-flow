using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Sampling;

public sealed class UniformTimeSamplingStrategy
    : ITimeSamplingStrategy
{
    public IReadOnlyList<double> GenerateTimes(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        TimeSamplingValidation.Validate(
            blowupParameters,
            seriesParameters);

        double timeStep =
            CalculateTimeStep(seriesParameters);

        double[] times =
            new double[seriesParameters.SampleCount];

        for (
            int index = 0;
            index < seriesParameters.SampleCount;
            index++)
        {
            times[index] = CalculateSampleTime(
                index,
                timeStep,
                seriesParameters);
        }

        return Array.AsReadOnly(times);
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