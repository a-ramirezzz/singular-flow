using SingularFlow.Api.Workers;

namespace SingularFlow.Api.Tests.Workers;

public sealed class SimulationJobWorkerOptionsTests
{
    [Fact]
    public void SectionName_IsApproved()
    {
        Assert.Equal(
            "SimulationJobs:Worker",
            SimulationJobWorkerOptions.SectionName);
    }

    [Fact]
    public void Defaults_AreApproved()
    {
        SimulationJobWorkerOptions options = new();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FailureDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RecoveryInterval);
        Assert.Equal(100, options.RecoveryBatchSize);
    }
}