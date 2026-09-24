using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.Simulations;

public sealed class ListSimulationsHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidPagination_DelegatesToRepository()
    {
        SimulationSummaryResult summary = new(
            Id: Guid.Parse(
                "89e58894-f2ab-4528-8030-75c727887611"),
            CreatedAtUtc: new DateTimeOffset(
                2026,
                9,
                24,
                12,
                0,
                0,
                TimeSpan.Zero),
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 5,
            SamplingMode: SamplingMode.Uniform);

        PagedSimulationResult expected = new(
            Items: new[] { summary },
            Page: 2,
            PageSize: 5,
            TotalCount: 8);

        RecordingSimulationRepository repository = new(
            expected);

        ListSimulationsHandler handler = new(
            repository);

        PagedSimulationResult actual =
            await handler.HandleAsync(
                page: 2,
                pageSize: 5,
                CancellationToken.None);

        Assert.Same(expected, actual);
        Assert.Equal(2, repository.RequestedPage);
        Assert.Equal(5, repository.RequestedPageSize);
        Assert.Equal(2, actual.TotalPages);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task HandleAsync_InvalidPagination_Throws(
        int page,
        int pageSize)
    {
        PagedSimulationResult emptyResult = new(
            Items: Array.Empty<SimulationSummaryResult>(),
            Page: 1,
            PageSize: 20,
            TotalCount: 0);

        RecordingSimulationRepository repository = new(
            emptyResult);

        ListSimulationsHandler handler = new(
            repository);

        await Assert.ThrowsAsync<
            ArgumentOutOfRangeException>(
                () => handler.HandleAsync(
                    page,
                    pageSize,
                    CancellationToken.None));
    }
    private sealed class RecordingSimulationRepository :
        ISimulationRepository
    {
        private readonly PagedSimulationResult _result;

        public RecordingSimulationRepository(
            PagedSimulationResult result)
        {
            _result = result;
        }

        public int? RequestedPage { get; private set; }

        public int? RequestedPageSize { get; private set; }

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
            throw new NotSupportedException();
        }

        public Task<PagedSimulationResult> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            RequestedPage = page;
            RequestedPageSize = pageSize;

            return Task.FromResult(_result);
        }
    }
}