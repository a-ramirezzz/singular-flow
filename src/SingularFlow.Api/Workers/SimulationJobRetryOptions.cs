namespace SingularFlow.Api.Workers;

internal sealed class SimulationJobRetryOptions
{
    public const string SectionName = "SimulationJobs:Retry";

    public int MaxAttempts { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } =
        TimeSpan.FromSeconds(5);

    public TimeSpan MaximumDelay { get; init; } =
        TimeSpan.FromMinutes(5);
}