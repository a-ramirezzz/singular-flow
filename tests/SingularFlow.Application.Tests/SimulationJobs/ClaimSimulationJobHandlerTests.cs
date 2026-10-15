using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class ClaimSimulationJobHandlerTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(
            year: 2026,
            month: 10,
            day: 1,
            hour: 12,
            minute: 0,
            second: 0,
            offset: TimeSpan.Zero);

    private static readonly DateTimeOffset ClaimedAtUtc =
        CreatedAtUtc.AddMinutes(1);

    [Fact]
    public async Task HandleAsync_ClaimedJob_DelegatesOnceAndReturnsRepositoryResult()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        SimulationJobResult repositoryResult = new(
            Id: Guid.Parse(
                "510cbb1d-5fd7-4c15-b05c-6c17ff6938ce"),
            Request: request,
            Status: SimulationJobStatus.Running,
            CreatedAtUtc: CreatedAtUtc,
            ClaimedAtUtc: ClaimedAtUtc,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);

        RecordingSimulationJobRepository repository = new(
            repositoryResult);
        ClaimSimulationJobHandler handler = new(repository);

        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            cancellation.Token);

        Assert.Equal(1, repository.InvocationCount);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedCancellationToken);
        Assert.Same(repositoryResult, result);
        Assert.NotNull(result);
        Assert.Equal(SimulationJobStatus.Running, result.Status);
        Assert.NotNull(result.ClaimedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_NoPendingJob_DelegatesOnceAndReturnsNull()
    {
        RecordingSimulationJobRepository repository = new(null);
        ClaimSimulationJobHandler handler = new(repository);

        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            cancellation.Token);

        Assert.Equal(1, repository.InvocationCount);
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

        public CancellationToken ReceivedCancellationToken
        {
            get;
            private set;
        }

        public Task<SimulationJobResult?> ClaimNextPendingAsync(
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ReceivedCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

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

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}