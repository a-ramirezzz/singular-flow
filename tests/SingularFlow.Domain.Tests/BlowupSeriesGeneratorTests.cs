using SingularFlow.Domain.Calculations;
using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

namespace SingularFlow.Domain.Tests;

public sealed class BlowupSeriesGeneratorTests
{
    private readonly BlowupParameters _blowupParameters =
        BlowupParameters.Default;

    private readonly TimeSeriesParameters _seriesParameters =
        new(
            startTime: 0.0,
            endTime: 0.8,
            sampleCount: 5);

    [Fact]
    public void Constructor_WhenCalculatorIsNull_ThrowsException()
    {
        ITimeSamplingStrategy samplingStrategy =
            new UniformTimeSamplingStrategy();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => new BlowupSeriesGenerator(
                    calculator: null!,
                    samplingStrategy: samplingStrategy));

        Assert.Equal(
            "calculator",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenSamplingStrategyIsNull_ThrowsException()
    {
        BlowupScalingCalculator calculator = new();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => new BlowupSeriesGenerator(
                    calculator: calculator,
                    samplingStrategy: null!));

        Assert.Equal(
            "samplingStrategy",
            exception.ParamName);
    }

    [Fact]
    public void Generate_WhenStrategyProvidesTimes_CalculatesStatesForThoseTimes()
    {
        double[] expectedTimes =
        [
            0.0,
            0.1,
            0.4,
            0.7,
            0.8
        ];

        ITimeSamplingStrategy samplingStrategy =
            new StubTimeSamplingStrategy(
                expectedTimes);

        BlowupSeriesGenerator generator = new(
            new BlowupScalingCalculator(),
            samplingStrategy);

        IReadOnlyList<BlowupState> states =
            generator.Generate(
                _blowupParameters,
                _seriesParameters);

        Assert.Equal(expectedTimes.Length, states.Count);

        for (int index = 0; index < expectedTimes.Length; index++)
        {
            Assert.Equal(
                expectedTimes[index],
                states[index].Time,
                precision: 12);
        }
    }

    [Fact]
    public void Generate_WhenConfigurationIsValid_ReturnsRequestedSampleCount()
    {
        BlowupSeriesGenerator generator =
            CreateGenerator();

        IReadOnlyList<BlowupState> states =
            generator.Generate(
                _blowupParameters,
                _seriesParameters);

        Assert.Equal(
            _seriesParameters.SampleCount,
            states.Count);
    }

    [Fact]
    public void Generate_WhenConfigurationIsValid_IncludesBothEndpoints()
    {
        BlowupSeriesGenerator generator =
            CreateGenerator();

        IReadOnlyList<BlowupState> states =
            generator.Generate(
                _blowupParameters,
                _seriesParameters);

        Assert.Equal(
            _seriesParameters.StartTime,
            states[0].Time);

        Assert.Equal(
            _seriesParameters.EndTime,
            states[^1].Time);
    }

    [Fact]
    public void Generate_WhenConfigurationIsValid_UsesUniformTimeSpacing()
    {
        BlowupSeriesGenerator generator =
            CreateGenerator();

        IReadOnlyList<BlowupState> states =
            generator.Generate(
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

        Assert.Equal(expectedTimes.Length, states.Count);

        for (int index = 0; index < expectedTimes.Length; index++)
        {
            Assert.Equal(
                expectedTimes[index],
                states[index].Time,
                precision: 12);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.1)]
    public void Generate_WhenEndTimeReachesOrExceedsSingularTime_ThrowsException(
        double endTime)
    {
        BlowupSeriesGenerator generator =
            CreateGenerator();

        TimeSeriesParameters seriesParameters = new(
            startTime: 0.0,
            endTime: endTime,
            sampleCount: 5);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => generator.Generate(
                    _blowupParameters,
                    seriesParameters));

        Assert.Equal(
            "seriesParameters",
            exception.ParamName);
    }

    [Fact]
    public void Generate_WhenBlowupParametersAreNull_ThrowsException()
    {
        BlowupSeriesGenerator generator =
            CreateGenerator();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => generator.Generate(
                    blowupParameters: null!,
                    seriesParameters: _seriesParameters));

        Assert.Equal(
            "blowupParameters",
            exception.ParamName);
    }

    [Fact]
    public void Generate_WhenSeriesParametersAreNull_ThrowsException()
    {
        BlowupSeriesGenerator generator =
            CreateGenerator();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => generator.Generate(
                    blowupParameters: _blowupParameters,
                    seriesParameters: null!));

        Assert.Equal(
            "seriesParameters",
            exception.ParamName);
    }

    private static BlowupSeriesGenerator CreateGenerator()
    {
        BlowupScalingCalculator calculator = new();

        ITimeSamplingStrategy samplingStrategy =
            new UniformTimeSamplingStrategy();

        return new BlowupSeriesGenerator(
            calculator,
            samplingStrategy);
    }

    private sealed class StubTimeSamplingStrategy
        : ITimeSamplingStrategy
    {
        private readonly IReadOnlyList<double> _times;

        public StubTimeSamplingStrategy(
            IReadOnlyList<double> times)
        {
            _times = times;
        }

        public IReadOnlyList<double> GenerateTimes(
            BlowupParameters blowupParameters,
            TimeSeriesParameters seriesParameters)
        {
            return _times;
        }
    }
}