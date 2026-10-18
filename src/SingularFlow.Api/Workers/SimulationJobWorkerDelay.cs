namespace SingularFlow.Api.Workers;

internal sealed class SimulationJobWorkerDelay :
    ISimulationJobWorkerDelay
{
    public Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        return Task.Delay(
            delay,
            cancellationToken);
    }
}