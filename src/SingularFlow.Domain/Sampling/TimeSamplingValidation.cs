using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Sampling;

internal static class TimeSamplingValidation
{
    public static void Validate(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters)
    {
        ArgumentNullException.ThrowIfNull(
            blowupParameters);

        ArgumentNullException.ThrowIfNull(
            seriesParameters);

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