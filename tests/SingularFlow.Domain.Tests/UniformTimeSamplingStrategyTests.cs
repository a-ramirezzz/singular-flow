using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

namespace SingularFlow.Domain.Tests;

public sealed class UniformTimeSamplingStrategyTests
{
    private readonly BlowupParameters _blowupParameters =
        BlowupParameters.Default;

    private readonly TimeSeriesParameters _seriesParameters =
        new(
            startTime: 0.0,
            endTime: 0.8,
            sampleCount: 5);

    [Fact]
    public void GenerateTimes_WhenConfigurationIsValid_ReturnsRequestedSampleCount()
    {
        UniformTimeSamplingStrategy strategy = new();

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
        UniformTimeSamplingStrategy strategy = new();

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
    public void GenerateTimes_WhenConfigurationIsValid_UsesUniformSpacing()
    {
        UniformTimeSamplingStrategy strategy = new();

        IReadOnlyList<double> times =
            strategy.GenerateTimes(
                _blowupParameters,
                _seriesParameters);

        double[] expectedTimes =
        [
            0.0,
            0.2,
            0.4,
            0.6,
            0.8
        ];

        Assert.Equal(expectedTimes.Length, times.Count);

        for (int index = 0; index < expectedTimes.Length; index++)
        {
            Assert.Equal(
                expectedTimes[index],
                times[index],
                precision: 12);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.1)]
    public void GenerateTimes_WhenEndTimeReachesOrExceedsSingularTime_ThrowsException(
        double endTime)
    {
        UniformTimeSamplingStrategy strategy = new();

        TimeSeriesParameters seriesParameters = new(
            startTime: 0.0,
            endTime: endTime,
            sampleCount: 5);

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
        UniformTimeSamplingStrategy strategy = new();

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
        UniformTimeSamplingStrategy strategy = new();

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