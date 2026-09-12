using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Sampling;

public interface ITimeSamplingStrategy
{
    IReadOnlyList<double> GenerateTimes(
        BlowupParameters blowupParameters,
        TimeSeriesParameters seriesParameters);
}