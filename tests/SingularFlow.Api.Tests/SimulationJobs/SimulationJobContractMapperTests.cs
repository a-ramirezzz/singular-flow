using SingularFlow.Api.Contracts.SimulationJobs;
using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.SimulationJobs;

public sealed class SimulationJobContractMapperTests
{
    [Theory]
    [InlineData(SimulationJobStatus.Pending, "pending")]
    [InlineData(SimulationJobStatus.Running, "running")]
    [InlineData(SimulationJobStatus.Completed, "completed")]
    [InlineData(SimulationJobStatus.Failed, "failed")]
    [InlineData(SimulationJobStatus.Cancelled, "cancelled")]
    public void ToApi_MapsSupportedStatus(
        SimulationJobStatus status,
        string expected)
    {
        SimulationJobApiResponse response =
            SimulationJobContractMapper.ToApi(
                CreateResult(status));

        Assert.Equal(expected, response.Status);
    }

    [Theory]
    [InlineData(SamplingMode.Uniform, "uniform")]
    [InlineData(SamplingMode.Logarithmic, "logarithmic")]
    public void ToApi_MapsSupportedSamplingMode(
        SamplingMode mode,
        string expected)
    {
        SimulationJobApiResponse response =
            SimulationJobContractMapper.ToApi(
                CreateResult(
                    SimulationJobStatus.Pending,
                    mode));

        Assert.Equal(expected, response.Request.SamplingMode);
    }

    [Fact]
    public void ToApi_PreservesCompletedSnapshotWithNullSimulation()
    {
        SimulationJobResult result = CreateResult(
            SimulationJobStatus.Completed);

        SimulationJobApiResponse response =
            SimulationJobContractMapper.ToApi(result);

        Assert.Equal(result.Id, response.Id);
        Assert.Equal(result.Request.SingularTime, response.Request.SingularTime);
        Assert.Equal(
            result.Request.ConcentrationExponent,
            response.Request.ConcentrationExponent);
        Assert.Equal(result.Request.StartTime, response.Request.StartTime);
        Assert.Equal(result.Request.EndTime, response.Request.EndTime);
        Assert.Equal(result.Request.SampleCount, response.Request.SampleCount);
        Assert.Equal(result.CreatedAtUtc, response.CreatedAtUtc);
        Assert.Null(response.ClaimedAtUtc);
        Assert.Null(response.FinishedAtUtc);
        Assert.Null(response.SimulationId);
        Assert.Null(response.FailureMessage);
        Assert.Equal("completed", response.Status);
    }

    [Fact]
    public void ToApi_NullResult_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => SimulationJobContractMapper.ToApi(null!));
    }

    [Fact]
    public void ToApi_UnsupportedStatus_Throws()
    {
        SimulationJobResult result = CreateResult(
            (SimulationJobStatus)int.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SimulationJobContractMapper.ToApi(result));
    }

    [Fact]
    public void ToApi_UnsupportedSamplingMode_Throws()
    {
        SimulationJobResult result = CreateResult(
            SimulationJobStatus.Pending,
            (SamplingMode)int.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SimulationJobContractMapper.ToApi(result));
    }

    private static SimulationJobResult CreateResult(
        SimulationJobStatus status,
        SamplingMode samplingMode = SamplingMode.Uniform)
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 5,
            SamplingMode: samplingMode);

        return new SimulationJobResult(
            Id: Guid.Parse(
                "89e58894-f2ab-4528-8030-75c727887611"),
            Request: request,
            Status: status,
            CreatedAtUtc: new DateTimeOffset(
                2026,
                10,
                18,
                12,
                0,
                0,
                TimeSpan.Zero),
            ClaimedAtUtc: null,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);
    }
}