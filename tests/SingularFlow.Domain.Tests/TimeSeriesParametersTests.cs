using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Tests;

public sealed class TimeSeriesParametersTests
{
    [Fact]
    public void Constructor_WhenValuesAreValid_SetsProperties()
    {
        TimeSeriesParameters parameters = new(
            startTime: 0.0,
            endTime: 0.9999,
            sampleCount: 6);

        Assert.Equal(0.0, parameters.StartTime);
        Assert.Equal(0.9999, parameters.EndTime);
        Assert.Equal(6, parameters.SampleCount);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.1)]
    public void Constructor_WhenStartTimeIsInvalid_ThrowsException(
        double startTime)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TimeSeriesParameters(
                    startTime: startTime,
                    endTime: 0.9,
                    sampleCount: 10));

        Assert.Equal("startTime", exception.ParamName);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_WhenEndTimeIsNotFinite_ThrowsException(
        double endTime)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TimeSeriesParameters(
                    startTime: 0.0,
                    endTime: endTime,
                    sampleCount: 10));

        Assert.Equal("endTime", exception.ParamName);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.5, 0.4)]
    public void Constructor_WhenEndTimeIsNotGreaterThanStartTime_ThrowsException(
        double startTime,
        double endTime)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TimeSeriesParameters(
                    startTime: startTime,
                    endTime: endTime,
                    sampleCount: 10));

        Assert.Equal("endTime", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100_001)]
    public void Constructor_WhenSampleCountIsOutsideAllowedRange_ThrowsException(
        int sampleCount)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TimeSeriesParameters(
                    startTime: 0.0,
                    endTime: 0.9,
                    sampleCount: sampleCount));

        Assert.Equal("sampleCount", exception.ParamName);
    }
}