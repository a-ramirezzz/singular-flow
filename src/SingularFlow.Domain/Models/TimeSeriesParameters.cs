namespace SingularFlow.Domain.Models;

public sealed record TimeSeriesParameters
{
    public const int MinimumSampleCount = 2;

    public const int MaximumSampleCount = 100_000;

    public double StartTime { get; }

    public double EndTime { get; }

    public int SampleCount { get; }

    public TimeSeriesParameters(
        double startTime,
        double endTime,
        int sampleCount)
    {
        ValidateStartTime(startTime);
        ValidateEndTime(endTime, startTime);
        ValidateSampleCount(sampleCount);

        StartTime = startTime;
        EndTime = endTime;
        SampleCount = sampleCount;
    }

    private static void ValidateStartTime(double startTime)
    {
        if (!double.IsFinite(startTime) || startTime < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startTime),
                "Start time must be a finite number " +
                "greater than or equal to zero.");
        }
    }

    private static void ValidateEndTime(
        double endTime,
        double startTime)
    {
        if (!double.IsFinite(endTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTime),
                "End time must be a finite number.");
        }

        if (endTime <= startTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTime),
                "End time must be greater than start time.");
        }
    }

    private static void ValidateSampleCount(int sampleCount)
    {
        if (sampleCount < MinimumSampleCount ||
            sampleCount > MaximumSampleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                $"Sample count must be between " +
                $"{MinimumSampleCount} and " +
                $"{MaximumSampleCount}.");
        }
    }
}