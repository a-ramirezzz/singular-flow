namespace SingularFlow.Api.Workers;

internal sealed class SimulationJobWorkerOptions
{
    public const string SectionName = "SimulationJobs:Worker";

    public bool Enabled { get; init; } = true;

    public TimeSpan PollingInterval { get; init; } =
        TimeSpan.FromSeconds(5);

    public TimeSpan FailureDelay { get; init; } =
        TimeSpan.FromSeconds(10);

    public TimeSpan LeaseDuration { get; init; } =
        TimeSpan.FromSeconds(30);

    public TimeSpan HeartbeatInterval { get; init; } =
        TimeSpan.FromSeconds(10);

    public TimeSpan RecoveryInterval { get; init; } =
        TimeSpan.FromSeconds(30);

    public int RecoveryBatchSize { get; init; } = 100;
}