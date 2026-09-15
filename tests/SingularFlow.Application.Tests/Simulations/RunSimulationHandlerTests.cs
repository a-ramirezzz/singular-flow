using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.Simulations;

public sealed class RunSimulationHandlerTests
{
    [Fact]
    public void Handle_WhenRequestIsNull_ThrowsException()
    {
        RunSimulationHandler handler = new();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => handler.Handle(
                    request: null!));

        Assert.Equal(
            "request",
            exception.ParamName);
    }

    [Fact]
    public void Handle_WhenUniformSamplingIsSelected_ReturnsUniformStates()
    {
        RunSimulationHandler handler = new();

        RunSimulationRequest request = CreateRequest(
            samplingMode: SamplingMode.Uniform,
            endTime: 0.8,
            sampleCount: 5);

        RunSimulationResult result =
            handler.Handle(request);

        double[] expectedTimes =
        [
            0.0,
            0.2,
            0.4,
            0.6,
            0.8
        ];

        Assert.Equal(
            expectedTimes.Length,
            result.States.Count);

        for (
            int index = 0;
            index < expectedTimes.Length;
            index++)
        {
            Assert.Equal(
                expectedTimes[index],
                result.States[index].Time,
                precision: 12);
        }
    }

    [Fact]
    public void Handle_WhenLogarithmicSamplingIsSelected_ReturnsLogarithmicStates()
    {
        RunSimulationHandler handler = new();

        RunSimulationRequest request = CreateRequest(
            samplingMode: SamplingMode.Logarithmic,
            endTime: 0.9999,
            sampleCount: 6);

        RunSimulationResult result =
            handler.Handle(request);

        Assert.Equal(
            6,
            result.States.Count);

        Assert.Equal(
            0.0,
            result.States[0].Time);

        Assert.Equal(
            0.8415106807538887,
            result.States[1].Time,
            precision: 12);

        Assert.Equal(
            0.9999,
            result.States[^1].Time);
    }

    [Fact]
    public void Handle_WhenRequestIsValid_ReturnsValidatedConfiguration()
    {
        RunSimulationHandler handler = new();

        RunSimulationRequest request = CreateRequest(
            samplingMode: SamplingMode.Uniform,
            endTime: 0.8,
            sampleCount: 5);

        RunSimulationResult result =
            handler.Handle(request);

        Assert.Equal(
            request.SingularTime,
            result.BlowupParameters.SingularTime);

        Assert.Equal(
            request.ConcentrationExponent,
            result.BlowupParameters.ConcentrationExponent);

        Assert.Equal(
            request.StartTime,
            result.TimeSeriesParameters.StartTime);

        Assert.Equal(
            request.EndTime,
            result.TimeSeriesParameters.EndTime);

        Assert.Equal(
            request.SampleCount,
            result.TimeSeriesParameters.SampleCount);

        Assert.Equal(
            request.SamplingMode,
            result.SamplingMode);
    }

    [Fact]
    public void Handle_WhenSamplingModeIsUnsupported_ThrowsException()
    {
        RunSimulationHandler handler = new();

        RunSimulationRequest request = CreateRequest(
            samplingMode: (SamplingMode)999,
            endTime: 0.8,
            sampleCount: 5);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => handler.Handle(request));

        Assert.Equal(
            "samplingMode",
            exception.ParamName);
    }

    private static RunSimulationRequest CreateRequest(
        SamplingMode samplingMode,
        double endTime,
        int sampleCount)
    {
        return new RunSimulationRequest(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: endTime,
            SampleCount: sampleCount,
            SamplingMode: samplingMode);
    }
}