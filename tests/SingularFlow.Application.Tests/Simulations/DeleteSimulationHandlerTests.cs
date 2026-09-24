namespace SingularFlow.Application.Tests.Simulations;

using SingularFlow.Application.Simulations;

public sealed class DeleteSimulationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ExistingId_DelegatesAndReturnsTrue()
    {
        Guid simulationId = Guid.NewGuid();

        RecordingSimulationRepository repository = new(
            deleteResult: true);

        DeleteSimulationHandler handler = new(
            repository);

        using CancellationTokenSource cancellation = new();

        bool deleted =
            await handler.HandleAsync(
                simulationId,
                cancellation.Token);

        Assert.True(deleted);
        Assert.Equal(
            simulationId,
            repository.DeletedId);

        Assert.Equal(
            cancellation.Token,
            repository.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_MissingId_ReturnsFalse()
    {
        RecordingSimulationRepository repository = new(
            deleteResult: false);

        DeleteSimulationHandler handler = new(
            repository);

        bool deleted =
            await handler.HandleAsync(
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.False(deleted);
    }

    private sealed class RecordingSimulationRepository :
        ISimulationRepository
    {
        private readonly bool _deleteResult;

        public RecordingSimulationRepository(
            bool deleteResult)
        {
            _deleteResult = deleteResult;
        }

        public Guid? DeletedId { get; private set; }

        public CancellationToken CancellationToken
        {
            get;
            private set;
        }

        public Task<bool> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            DeletedId = id;
            CancellationToken = cancellationToken;

            return Task.FromResult(
                _deleteResult);
        }

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
            throw new NotSupportedException();
        }
    }
}