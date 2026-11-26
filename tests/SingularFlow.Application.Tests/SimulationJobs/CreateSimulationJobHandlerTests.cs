using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class CreateSimulationJobHandlerTests
{
    private static readonly Guid JobId =
        Guid.Parse(
            "89e58894-f2ab-4528-8030-75c727887611");

    private static readonly DateTimeOffset CreatedAtUtc =
        new(
            year: 2026,
            month: 10,
            day: 1,
            hour: 12,
            minute: 0,
            second: 0,
            offset: TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Request_DelegatesOnceAndReturnsRepositoryResult()
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
            CreatedAtUtc: CreatedAtUtc,
            ClaimedAtUtc: null,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);

        RecordingSimulationJobRepository repository = new(
            repositoryResult);
        SimulationJobRetryPolicy retryPolicy = new(
            maxAttempts: 7,
            baseDelay: TimeSpan.FromSeconds(2),
            maximumDelay: TimeSpan.FromMinutes(1));

        CreateSimulationJobHandler handler = new(
            repository,
            retryPolicy);

        using CancellationTokenSource cancellation = new();

        SimulationJobResult result =
            await handler.HandleAsync(
                request,
                cancellation.Token);

        Assert.Equal(1, repository.InvocationCount);
        Assert.Same(request, repository.ReceivedRequest);
        Assert.Equal(7, repository.ReceivedMaxAttempts);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedCancellationToken);
        Assert.Same(repositoryResult, result);
        Assert.Same(request, result.Request);
    }

    [Fact]
    public async Task HandleAsync_InvalidRequest_ThrowsBeforeRepositoryInvocation()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 1.0,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        SimulationJobResult repositoryResult = new(
            Id: JobId,
            Request: request,
            Status: SimulationJobStatus.Pending,
            CreatedAtUtc: CreatedAtUtc,
            ClaimedAtUtc: null,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);

        RecordingSimulationJobRepository repository = new(
            repositoryResult);

        CreateSimulationJobHandler handler = new(
            repository,
            SimulationJobRetryPolicy.Default);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => handler.HandleAsync(
                    request,
                    CancellationToken.None));

        Assert.Equal("seriesParameters", exception.ParamName);
        Assert.Equal(0, repository.InvocationCount);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedSamplingMode_ThrowsBeforeRepositoryInvocation()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: (SamplingMode)999);
        SimulationJobResult repositoryResult = new(
            Id: JobId,
            Request: request,
            Status: SimulationJobStatus.Pending,
            CreatedAtUtc: CreatedAtUtc,
            ClaimedAtUtc: null,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);
        RecordingSimulationJobRepository repository = new(repositoryResult);
        CreateSimulationJobHandler handler = new(
            repository,
            SimulationJobRetryPolicy.Default);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => handler.HandleAsync(
                    request,
                    CancellationToken.None));

        Assert.Equal("samplingMode", exception.ParamName);
        Assert.Equal(0, repository.InvocationCount);
    }

    [Fact]
    public async Task HandleAsync_DefaultPolicy_ForwardsThreeAttempts()
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
            CreatedAtUtc: CreatedAtUtc,
            ClaimedAtUtc: null,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);
        RecordingSimulationJobRepository repository = new(repositoryResult);
        CreateSimulationJobHandler handler = new(
            repository,
            SimulationJobRetryPolicy.Default);

        await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(3, repository.ReceivedMaxAttempts);
    }

    [Fact]
    public void Constructor_NullRetryPolicy_ThrowsArgumentNullException()
    {
        RecordingSimulationJobRepository repository = new(
            new SimulationJobResult(
                Id: JobId,
                Request: null!,
                Status: SimulationJobStatus.Pending,
                CreatedAtUtc: CreatedAtUtc,
                ClaimedAtUtc: null,
                SimulationId: null,
                FinishedAtUtc: null,
                FailureMessage: null));

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new CreateSimulationJobHandler(repository, null!));

        Assert.Equal("retryPolicy", exception.ParamName);
    }

    [Fact]
    public void SimulationJobStatus_DefinesExpectedLifecycleValues()
    {
        string[] names =
            Enum.GetNames<SimulationJobStatus>();

        Assert.Equal(
            new[]
            {
                "Pending",
                "Running",
                "Completed",
                "Failed",
                "Cancelled"
            },
            names);

        Assert.Equal(0, (int)SimulationJobStatus.Pending);
        Assert.Equal(1, (int)SimulationJobStatus.Running);
        Assert.Equal(2, (int)SimulationJobStatus.Completed);
        Assert.Equal(3, (int)SimulationJobStatus.Failed);
        Assert.Equal(4, (int)SimulationJobStatus.Cancelled);
    }

    private sealed class RecordingSimulationJobRepository :
        ISimulationJobRepository
    {
        private readonly SimulationJobResult _result;

        public RecordingSimulationJobRepository(
            SimulationJobResult result)
        {
            _result = result;
        }

        public int InvocationCount { get; private set; }

        public RunSimulationRequest? ReceivedRequest
        {
            get;
            private set;
        }

        public CancellationToken ReceivedCancellationToken
        {
            get;
            private set;
        }

        public int ReceivedMaxAttempts { get; private set; }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            int maxAttempts,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ReceivedRequest = request;
            ReceivedMaxAttempts = maxAttempts;
            ReceivedCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid claimToken,
            Guid simulationId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            Guid claimToken,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}