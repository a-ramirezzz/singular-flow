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

    [Fact]
    public async Task CompleteHandler_RepositoryResult_DelegatesAndReturnsExactResult()
    {
        SimulationJobResult repositoryResult = CreateResult(
            SimulationJobStatus.Completed);
        RecordingSimulationJobRepository repository = new(
            repositoryResult);
        CompleteSimulationJobHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            SimulationId,
            cancellation.Token);

        Assert.Equal(1, repository.CompletionInvocationCount);
        Assert.Equal(JobId, repository.ReceivedJobId);
        Assert.Equal(SimulationId, repository.ReceivedSimulationId);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedCompletionCancellationToken);
        Assert.Same(repositoryResult, result);
    }

    [Fact]
    public async Task CompleteHandler_NoTransition_PropagatesNull()
    {
        RecordingSimulationJobRepository repository = new(null);
        CompleteSimulationJobHandler handler = new(repository);

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            SimulationId,
            CancellationToken.None);

        Assert.Equal(1, repository.CompletionInvocationCount);
        Assert.Null(result);
    }

    [Fact]
    public async Task FailHandler_RepositoryResult_DelegatesAndReturnsExactResult()
    {
        const string failureMessage =
            "The simulation could not be calculated.";
        SimulationJobResult repositoryResult = CreateResult(
            SimulationJobStatus.Failed);
        RecordingSimulationJobRepository repository = new(
            repositoryResult);
        FailSimulationJobHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            failureMessage,
            cancellation.Token);

        Assert.Equal(1, repository.FailureInvocationCount);
        Assert.Equal(JobId, repository.ReceivedJobId);
        Assert.Equal(failureMessage, repository.ReceivedFailureMessage);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedFailureCancellationToken);
        Assert.Same(repositoryResult, result);
    }

    [Fact]
    public async Task FailHandler_NoTransition_PropagatesNull()
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        SimulationJobResult? result = await handler.HandleAsync(
            JobId,
            "Failure",
            CancellationToken.None);

        Assert.Equal(1, repository.FailureInvocationCount);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task FailHandler_InvalidMessage_ThrowsAndDoesNotCallRepository(
        string failureMessage)
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        ArgumentException exception =
            await Assert.ThrowsAsync<ArgumentException>(
                () => handler.HandleAsync(
                    JobId,
                    failureMessage,
                    CancellationToken.None));

        Assert.Equal("failureMessage", exception.ParamName);
        Assert.Equal(0, repository.FailureInvocationCount);
    }

    [Fact]
    public async Task FailHandler_NullMessage_ThrowsAndDoesNotCallRepository()
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => handler.HandleAsync(
                    JobId,
                    null!,
                    CancellationToken.None));

        Assert.Equal("failureMessage", exception.ParamName);
        Assert.Equal(0, repository.FailureInvocationCount);
    }

    [Fact]
    public async Task FailHandler_MessageOverMaximumLength_ThrowsAndDoesNotCallRepository()
    {
        RecordingSimulationJobRepository repository = new(null);
        FailSimulationJobHandler handler = new(repository);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => handler.HandleAsync(
                    JobId,
                    new string('x', 1001),
                    CancellationToken.None));

        Assert.Equal("failureMessage", exception.ParamName);
        Assert.Equal(0, repository.FailureInvocationCount);
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

        public int CompletionInvocationCount { get; private set; }

        public int FailureInvocationCount { get; private set; }

        public Guid ReceivedJobId { get; private set; }

        public Guid ReceivedSimulationId { get; private set; }

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
            Guid simulationId,
            CancellationToken cancellationToken)
        {
            CompletionInvocationCount++;
            ReceivedJobId = jobId;
            ReceivedSimulationId = simulationId;
            ReceivedCompletionCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            FailureInvocationCount++;
            ReceivedJobId = jobId;
            ReceivedFailureMessage = failureMessage;
            ReceivedFailureCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult?> ClaimNextPendingAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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