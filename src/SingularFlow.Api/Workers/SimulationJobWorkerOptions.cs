namespace SingularFlow.Api.Workers;

internal sealed class SimulationJobWorkerOptions
{
    public const string SectionName = "SimulationJobs:Worker";

    public bool Enabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(5);

    public TimeSpan FailureDelay { get; set; } =
        TimeSpan.FromSeconds(10);
}