using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

namespace SingularFlow.Domain.Tests;

public sealed class LogarithmicTimeSamplingStrategyTests
{
    private readonly BlowupParameters _blowupParameters =
        BlowupParameters.Default;

    private readonly TimeSeriesParameters _seriesParameters =
        new(
            startTime: 0.0,
            endTime: 0.9999,
            sampleCount: 6);

    [Fact]
    public void GenerateTimes_WhenConfigurationIsValid_ReturnsRequestedSampleCount()
    {
        LogarithmicTimeSamplingStrategy strategy = new();

        IReadOnlyList<double> times =
            strategy.GenerateTimes(
                _blowupParameters,
                _seriesParameters);

        Assert.Equal(
            _seriesParameters.SampleCount,
            times.Count);
    }

    [Fact]
    public void GenerateTimes_WhenConfigurationIsValid_IncludesBothEndpoints()
    {
        LogarithmicTimeSamplingStrategy strategy = new();

        IReadOnlyList<double> times =
            strategy.GenerateTimes(
                _blowupParameters,
                _seriesParameters);

        Assert.Equal(
            _seriesParameters.StartTime,
            times[0]);

        Assert.Equal(
            _seriesParameters.EndTime,
            times[^1]);
    }

    [Fact]
    public void GenerateTimes_WhenConfigurationIsValid_UsesGeometricRemainingTimeSpacing()
    {
        LogarithmicTimeSamplingStrategy strategy = new();

        IReadOnlyList<double> times =
            strategy.GenerateTimes(
                _blowupParameters,
                _seriesParameters);

        double[] expectedRemainingTimes =
        [
            1.0,
            0.15848931924611134,
            0.025118864315095798,
            0.003981071705534972,
            0.000630957344480193,
            0.0001
        ];

        Assert.Equal(
            expectedRemainingTimes.Length,
            times.Count);

        for (
            int index = 0;
            index < expectedRemainingTimes.Length;
            index++)
        {
            double actualRemainingTime =
                _blowupParameters.SingularTime -
                times[index];

            Assert.Equal(
                expectedRemainingTimes[index],
                actualRemainingTime,
                precision: 12);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.1)]
    public void GenerateTimes_WhenEndTimeReachesOrExceedsSingularTime_ThrowsException(
        double endTime)
    {
        LogarithmicTimeSamplingStrategy strategy = new();

        TimeSeriesParameters seriesParameters = new(
            startTime: 0.0,
            endTime: endTime,
            sampleCount: 6);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => strategy.GenerateTimes(
                    _blowupParameters,
                    seriesParameters));

        Assert.Equal(
            "seriesParameters",
            exception.ParamName);
    }

    [Fact]
    public void GenerateTimes_WhenBlowupParametersAreNull_ThrowsException()
    {
        LogarithmicTimeSamplingStrategy strategy = new();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => strategy.GenerateTimes(
                    blowupParameters: null!,
                    seriesParameters: _seriesParameters));

        Assert.Equal(
            "blowupParameters",
            exception.ParamName);
    }

    [Fact]
    public void GenerateTimes_WhenSeriesParametersAreNull_ThrowsException()
    {
        LogarithmicTimeSamplingStrategy strategy = new();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => strategy.GenerateTimes(
                    blowupParameters: _blowupParameters,
                    seriesParameters: null!));

        Assert.Equal(
            "seriesParameters",
            exception.ParamName);
    }
}