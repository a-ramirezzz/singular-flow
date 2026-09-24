using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.Simulations;

public sealed class GetSimulationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ExistingSimulation_ReturnsPersistedResult()
    {
        PersistedSimulationResult expected =
            CreatePersistedSimulation();

        StubSimulationRepository repository = new(
            expected);

        GetSimulationHandler handler = new(
            repository);

        PersistedSimulationResult? actual =
            await handler.HandleAsync(
                expected.Id,
                CancellationToken.None);

        Assert.Same(expected, actual);

        Assert.Equal(
            expected.Id,
            repository.RequestedId);
    }

    [Fact]
    public async Task HandleAsync_MissingSimulation_ReturnsNull()
    {
        StubSimulationRepository repository = new(
            result: null);

        GetSimulationHandler handler = new(
            repository);

        PersistedSimulationResult? actual =
            await handler.HandleAsync(
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.Null(actual);
    }

    private static PersistedSimulationResult
        CreatePersistedSimulation()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        RunSimulationResult simulation =
            new RunSimulationHandler().Handle(request);

        return new PersistedSimulationResult(
            Id: Guid.Parse(
                "89e58894-f2ab-4528-8030-75c727887611"),
            CreatedAtUtc: new DateTimeOffset(
                2026,
                9,
                23,
                12,
                0,
                0,
                TimeSpan.Zero),
            Simulation: simulation);
    }

    private sealed class StubSimulationRepository :
        ISimulationRepository
    {
        public Task<PagedSimulationResult> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
        private readonly PersistedSimulationResult? _result;

        public StubSimulationRepository(
            PersistedSimulationResult? result)
        {
            _result = result;
        }

        public Guid? RequestedId { get; private set; }

        public Task<PersistedSimulationResult> SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PersistedSimulationResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            RequestedId = id;

            return Task.FromResult(_result);
        }
    }
}