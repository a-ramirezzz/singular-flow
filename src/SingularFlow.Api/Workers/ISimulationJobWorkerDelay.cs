namespace SingularFlow.Api.Workers;

internal interface ISimulationJobWorkerDelay
{
    Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken);
}