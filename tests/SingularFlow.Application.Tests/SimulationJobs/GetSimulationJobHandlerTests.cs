using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class GetSimulationJobHandlerTests
{
    private static readonly Guid JobId =
        Guid.Parse(
            "56a03c91-470b-4913-b189-155815b31426");

    [Fact]
    public async Task HandleAsync_ExistingJob_DelegatesOnceAndReturnsRepositoryResult()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        SimulationJobResult repositoryResult = new(
            Id: JobId,
            Request: request,
            Status: SimulationJobStatus.Pending,
            CreatedAtUtc: new DateTimeOffset(
                2026,
                10,
                18,
                12,
                0,
                0,
                TimeSpan.Zero),
            ClaimedAtUtc: null,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);

        RecordingSimulationJobRepository repository = new(
            repositoryResult);
        GetSimulationJobHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            cancellation.Token);

        Assert.Equal(1, repository.InvocationCount);
        Assert.Equal(JobId, repository.ReceivedId);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedCancellationToken);
        Assert.Same(repositoryResult, result);
    }

    [Fact]
    public async Task HandleAsync_MissingJob_DelegatesOnceAndReturnsNull()
    {
        Guid missingJobId = Guid.Parse(
            "743ea857-461f-44ae-bf51-79e70f63bb6b");
        RecordingSimulationJobRepository repository = new(null);
        GetSimulationJobHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            missingJobId,
            cancellation.Token);

        Assert.Equal(1, repository.InvocationCount);
        Assert.Equal(missingJobId, repository.ReceivedId);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedCancellationToken);
        Assert.Null(result);
    }

    private sealed class RecordingSimulationJobRepository :
        ISimulationJobRepository
    {
        private readonly SimulationJobResult? _result;

        public RecordingSimulationJobRepository(
            SimulationJobResult? result)
        {
            _result = result;
        }

        public int InvocationCount { get; private set; }

        public Guid ReceivedId { get; private set; }

        public CancellationToken ReceivedCancellationToken
        {
            get;
            private set;
        }

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ReceivedId = id;
            ReceivedCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> ClaimNextPendingAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid simulationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            string failureMessage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}