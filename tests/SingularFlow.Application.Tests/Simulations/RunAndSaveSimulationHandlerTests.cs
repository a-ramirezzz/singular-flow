using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.Simulations;

public sealed class RunAndSaveSimulationHandlerTests
{
    private static readonly Guid PersistedId =
        Guid.Parse(
            "89e58894-f2ab-4528-8030-75c727887611");

    private static readonly DateTimeOffset PersistedAt =
        new(
            year: 2026,
            month: 9,
            day: 23,
            hour: 12,
            minute: 0,
            second: 0,
            offset: TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidRequest_SavesAndReturnsPersistedSimulation()
    {
        RecordingSimulationRepository repository = new();

        RunAndSaveSimulationHandler handler = new(
            new RunSimulationHandler(),
            repository);

        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        PersistedSimulationResult persisted =
            await handler.HandleAsync(
                request,
                CancellationToken.None);

        Assert.Equal(PersistedId, persisted.Id);
        Assert.Equal(PersistedAt, persisted.CreatedAtUtc);
        Assert.Same(
            repository.SavedResult,
            persisted.Simulation);

        Assert.Equal(3, persisted.Simulation.States.Count);
        Assert.Equal(
            0.0,
            persisted.Simulation.States[0].Time);

        Assert.Equal(
            0.4,
            persisted.Simulation.States[1].Time,
            precision: 10);

        Assert.Equal(
            0.8,
            persisted.Simulation.States[2].Time,
            precision: 10);
    }

    private sealed class RecordingSimulationRepository :
        ISimulationRepository
    {
        public Task<bool> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
        public Task<PagedSimulationResult> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
        public RunSimulationResult? SavedResult { get; private set; }

        public Task<PersistedSimulationResult> SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            SavedResult = result;

            PersistedSimulationResult persisted = new(
                Id: PersistedId,
                CreatedAtUtc: PersistedAt,
                Simulation: result);

            return Task.FromResult(persisted);
        }

        public Task<PersistedSimulationResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}