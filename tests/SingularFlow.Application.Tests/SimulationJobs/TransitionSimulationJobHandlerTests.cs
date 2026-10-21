using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class TransitionSimulationJobHandlerTests
{
    private static readonly Guid JobId =
        Guid.Parse(
            "6e3266db-01a9-4eaf-aeff-e607c36a33e6");

    private static readonly Guid SimulationId =
        Guid.Parse(
            "c5a427fe-e948-4f0d-86a5-4c5a93f819b0");

    private static readonly Guid ClaimToken =
        Guid.Parse(
            "013fa095-9aac-46ad-b335-1b46f092a91f");

    [Fact]
    public async Task CompleteHandler_ClaimToken_DelegatesAndReturnsExactResult()
    {
        SimulationJobResult repositoryResult = CreateResult(
            SimulationJobStatus.Completed);
        RecordingSimulationJobRepository repository = new(
            repositoryResult);
        CompleteSimulationJobHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            ClaimToken,
            SimulationId,
            cancellation.Token);

        Assert.Equal(1, repository.FencedCompletionInvocationCount);
        Assert.Equal(JobId, repository.ReceivedJobId);
        Assert.Equal(ClaimToken, repository.ReceivedClaimToken);
        Assert.Equal(SimulationId, repository.ReceivedSimulationId);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedCompletionCancellationToken);
        Assert.Same(repositoryResult, result);
    }

    [Fact]
    public async Task CompleteHandler_ClaimToken_NoTransition_PropagatesNull()
    {
        RecordingSimulationJobRepository repository = new(null);
        CompleteSimulationJobHandler handler = new(repository);

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            ClaimToken,
            SimulationId,
            CancellationToken.None);

        Assert.Equal(1, repository.FencedCompletionInvocationCount);
        Assert.Null(result);
    }

    [Fact]
    public async Task FailHandler_ClaimToken_DelegatesAndReturnsExactResult()
    {
        const string failureMessage = "  Exact failure description.  ";
        SimulationJobResult repositoryResult = CreateResult(
            SimulationJobStatus.Failed);
        RecordingSimulationJobRepository repository = new(
            repositoryResult);
        FailSimulationJobHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            ClaimToken,
            failureMessage,
            cancellation.Token);

        Assert.Equal(1, repository.FencedFailureInvocationCount);
        Assert.Equal(JobId, repository.ReceivedJobId);
        Assert.Equal(ClaimToken, repository.ReceivedClaimToken);
        Assert.Equal(failureMessage, repository.ReceivedFailureMessage);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedFailureCancellationToken);
        Assert.Same(repositoryResult, result);
    }

    [Fact]
    public async Task FailHandler_ClaimToken_NoTransition_PropagatesNull()
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            ClaimToken,
            "Failure",
            CancellationToken.None);

        Assert.Equal(1, repository.FencedFailureInvocationCount);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task FailHandler_ClaimToken_InvalidMessage_ThrowsBeforeRepository(
        string failureMessage)
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        ArgumentException exception =
            await Assert.ThrowsAsync<ArgumentException>(
                () => handler.HandleAsync(
                    JobId,
                    ClaimToken,
                    failureMessage,
                    CancellationToken.None));

        Assert.Equal("failureMessage", exception.ParamName);
        Assert.Equal(0, repository.FencedFailureInvocationCount);
    }

    [Fact]
    public async Task FailHandler_ClaimToken_NullMessage_ThrowsBeforeRepository()
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => handler.HandleAsync(
                    JobId,
                    ClaimToken,
                    null!,
                    CancellationToken.None));

        Assert.Equal("failureMessage", exception.ParamName);
        Assert.Equal(0, repository.FencedFailureInvocationCount);
    }

    [Fact]
    public async Task FailHandler_ClaimToken_OverMaximum_ThrowsBeforeRepository()
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => handler.HandleAsync(
                    JobId,
                    ClaimToken,
                    new string('x', 1001),
                    CancellationToken.None));

        Assert.Equal("failureMessage", exception.ParamName);
        Assert.Equal(0, repository.FencedFailureInvocationCount);
    }

    private static SimulationJobResult CreateResult(
        SimulationJobStatus status)
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        return new SimulationJobResult(
            Id: JobId,
            Request: request,
            Status: status,
            CreatedAtUtc: DateTimeOffset.UnixEpoch,
            ClaimedAtUtc: DateTimeOffset.UnixEpoch,
            SimulationId: status == SimulationJobStatus.Completed
                ? SimulationId
                : null,
            FinishedAtUtc: status is SimulationJobStatus.Completed or
                SimulationJobStatus.Failed
                ? DateTimeOffset.UnixEpoch
                : null,
            FailureMessage: status == SimulationJobStatus.Failed
                ? "The simulation failed."
                : null);
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

        public int FencedCompletionInvocationCount { get; private set; }

        public int FencedFailureInvocationCount { get; private set; }

        public Guid ReceivedJobId { get; private set; }

        public Guid ReceivedSimulationId { get; private set; }

        public Guid ReceivedClaimToken { get; private set; }

        public string? ReceivedFailureMessage { get; private set; }

        public CancellationToken ReceivedCompletionCancellationToken
        {
            get;
            private set;
        }

        public CancellationToken ReceivedFailureCancellationToken
        {
            get;
            private set;
        }

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid claimToken,
            Guid simulationId,
            CancellationToken cancellationToken)
        {
            FencedCompletionInvocationCount++;
            ReceivedJobId = jobId;
            ReceivedClaimToken = claimToken;
            ReceivedSimulationId = simulationId;
            ReceivedCompletionCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            Guid claimToken,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            FencedFailureInvocationCount++;
            ReceivedJobId = jobId;
            ReceivedClaimToken = claimToken;
            ReceivedFailureMessage = failureMessage;
            ReceivedFailureCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}