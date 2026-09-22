namespace SingularFlow.Application.Simulations;

public interface ISimulationRepository
{
    Task SaveAsync(
        RunSimulationResult result,
        CancellationToken cancellationToken);
}