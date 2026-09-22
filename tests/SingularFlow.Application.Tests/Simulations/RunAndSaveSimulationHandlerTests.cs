using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.Simulations;

public sealed class RunAndSaveSimulationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidRequest_SavesAndReturnsCalculatedSeries()
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

        RunSimulationResult result =
            await handler.HandleAsync(
                request,
                CancellationToken.None);

        Assert.Same(result, repository.SavedResult);
        Assert.Equal(3, result.States.Count);
        Assert.Equal(0.0, result.States[0].Time);
        Assert.Equal(0.4, result.States[1].Time, precision: 10);
        Assert.Equal(0.8, result.States[2].Time, precision: 10);
    }

    private sealed class RecordingSimulationRepository :
        ISimulationRepository
    {
        public RunSimulationResult? SavedResult { get; private set; }

        public Task SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            SavedResult = result;
            return Task.CompletedTask;
        }
    }
}