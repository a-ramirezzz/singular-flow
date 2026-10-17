using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class ProcessNextSimulationJobHandlerTests
{
    private static readonly Guid JobId =
        Guid.Parse("be2e28a4-7115-4b22-b799-60347477c6c3");

    private static readonly Guid SimulationId =
        Guid.Parse("7e418116-57ad-4a38-a279-4a0bd1d06998");

    private static readonly DateTimeOffset Timestamp =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_PreCancelledToken_ThrowsWithoutClaiming()
    {
        RecordingSimulationJobRepository jobs = new(null);
        RecordingCompletionRepository completion = new();
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleAsync(cancellation.Token));

        Assert.Equal(0, jobs.ClaimInvocationCount);
        Assert.Equal(0, completion.InvocationCount);
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Fact]
    public async Task HandleAsync_EmptyQueue_ReturnsNoWorkAndForwardsToken()
    {
        RecordingSimulationJobRepository jobs = new(null);
        RecordingCompletionRepository completion = new();
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);
        using CancellationTokenSource cancellation = new();

        ProcessNextSimulationJobResult result =
            await handler.HandleAsync(cancellation.Token);

        Assert.IsType<ProcessNextSimulationJobResult.NoWork>(result);
        Assert.Equal(1, jobs.ClaimInvocationCount);
        Assert.Equal(cancellation.Token, jobs.ClaimCancellationToken);
        Assert.Equal(0, completion.InvocationCount);
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Fact]
    public async Task HandleAsync_ValidClaim_CompletesWithExactRequestAndToken()
    {
        RunSimulationRequest request = new(
            SingularTime: 2.75,
            ConcentrationExponent: 0.004,
            StartTime: 0.25,
            EndTime: 1.75,
            SampleCount: 4,
            SamplingMode: SamplingMode.Uniform);
        SimulationJobResult claimed = CreateRunningJob(request);
        SimulationJobResult terminal = CreateCompletedJob(
            JobId,
            SimulationId);
        RecordingSimulationJobRepository jobs = new(claimed);
        RecordingCompletionRepository completion = new(terminal);
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);
        using CancellationTokenSource cancellation = new();

        ProcessNextSimulationJobResult result =
            await handler.HandleAsync(cancellation.Token);

        ProcessNextSimulationJobResult.Completed completed =
            Assert.IsType<ProcessNextSimulationJobResult.Completed>(
                result);
        Assert.Equal(JobId, completed.JobId);
        Assert.Equal(SimulationId, completed.SimulationId);
        Assert.Equal(1, jobs.ClaimInvocationCount);
        Assert.Equal(cancellation.Token, jobs.ClaimCancellationToken);
        Assert.Equal(1, completion.InvocationCount);
        Assert.Equal(JobId, completion.ReceivedJobId);
        Assert.Equal(
            cancellation.Token,
            completion.ReceivedCancellationToken);

        RunSimulationResult simulation =
            Assert.IsType<RunSimulationResult>(
                completion.ReceivedSimulation);
        Assert.Equal(request.SingularTime, simulation.BlowupParameters.SingularTime);
        Assert.Equal(
            request.ConcentrationExponent,
            simulation.BlowupParameters.ConcentrationExponent);
        Assert.Equal(request.StartTime, simulation.TimeSeriesParameters.StartTime);
        Assert.Equal(request.EndTime, simulation.TimeSeriesParameters.EndTime);
        Assert.Equal(request.SampleCount, simulation.TimeSeriesParameters.SampleCount);
        Assert.Equal(request.SamplingMode, simulation.SamplingMode);
        Assert.Equal(
            new[] { 0.25, 0.75, 1.25, 1.75 },
            simulation.States.Select(state => state.Time));
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Fact]
    public async Task HandleAsync_ClaimCancelsToken_ThrowsBeforeProcessing()
    {
        using CancellationTokenSource cancellation = new();
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob());
        jobs.OnClaim = () => cancellation.Cancel();
        RecordingCompletionRepository completion = new();
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleAsync(cancellation.Token));

        Assert.Equal(1, jobs.ClaimInvocationCount);
        Assert.Equal(0, completion.InvocationCount);
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Fact]
    public async Task HandleAsync_CalculationFails_MarksFailedAndRethrowsOriginal()
    {
        RunSimulationRequest invalidRequest = CreateRequest(
            samplingMode: (SamplingMode)999);
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob(invalidRequest));
        RecordingCompletionRepository completion = new();
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);
        using CancellationTokenSource cancellation = new();

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => handler.HandleAsync(cancellation.Token));

        Assert.Equal(1, jobs.FailureInvocationCount);
        Assert.Equal(JobId, jobs.FailedJobId);
        Assert.Equal(
            "Simulation execution failed.",
            jobs.FailureMessage);
        Assert.Equal(
            cancellation.Token,
            jobs.FailureCancellationToken);
        Assert.Contains(
            nameof(RunSimulationHandler.Handle),
            exception.StackTrace);
        Assert.Equal(0, completion.InvocationCount);
    }

    [Fact]
    public async Task HandleAsync_FinalizationFails_MarksFailedAndRethrowsOriginal()
    {
        InvalidOperationException original =
            new("sensitive finalization details");
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob());
        RecordingCompletionRepository completion = new()
        {
            Exception = original
        };
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);
        using CancellationTokenSource cancellation = new();

        Exception observed =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(cancellation.Token));

        Assert.Same(original, observed);
        Assert.Equal(1, completion.InvocationCount);
        Assert.Equal(JobId, completion.ReceivedJobId);
        Assert.Equal(cancellation.Token, completion.ReceivedCancellationToken);
        Assert.Equal(1, jobs.FailureInvocationCount);
        Assert.Equal(JobId, jobs.FailedJobId);
        Assert.Equal(
            "Simulation persistence or completion failed.",
            jobs.FailureMessage);
        Assert.DoesNotContain("sensitive", jobs.FailureMessage);
        Assert.Equal(
            cancellation.Token,
            jobs.FailureCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_FailureTransitionReturnsNull_AttachesSafeDiagnostic()
    {
        InvalidOperationException original = new("calculation failure");
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob())
        {
            FailureResult = null
        };
        RecordingCompletionRepository completion = new()
        {
            Exception = original
        };
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        Exception observed =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(CancellationToken.None));

        Assert.Same(original, observed);
        Assert.Equal(1, jobs.FailureInvocationCount);
        Assert.Contains(
            observed.Data.Values.Cast<object>(),
            value => value is string diagnostic &&
                diagnostic.Contains(
                    "transition returned no result",
                    StringComparison.Ordinal));
        Assert.Equal(1, completion.InvocationCount);
    }

    [Fact]
    public async Task HandleAsync_FailureTransitionThrows_AttachesSecondaryException()
    {
        InvalidOperationException original =
            new("sensitive finalization details");
        IOException secondary = new("failure recording details");
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob())
        {
            FailureException = secondary
        };
        RecordingCompletionRepository completion = new()
        {
            Exception = original
        };
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        Exception observed =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(CancellationToken.None));

        Assert.Same(original, observed);
        Assert.Contains(observed.Data.Values.Cast<object>(), value =>
            ReferenceEquals(value, secondary));
        Assert.DoesNotContain(
            observed.Data.Values.Cast<object>(),
            value => value is string diagnostic &&
                diagnostic.Contains(
                    secondary.Message,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_FailureTransitionCancels_AttachesCancellationOnlyAsDiagnostic()
    {
        InvalidOperationException original =
            new("original finalization failure");
        OperationCanceledException secondary =
            new("failure transition cancelled");
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob())
        {
            FailureException = secondary
        };
        RecordingCompletionRepository completion = new()
        {
            Exception = original
        };
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        Exception observed =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(CancellationToken.None));

        Assert.Same(original, observed);
        Assert.Contains(observed.Data.Values.Cast<object>(), value =>
            ReferenceEquals(value, secondary));
    }

    [Fact]
    public async Task HandleAsync_FinalizationCancellation_DoesNotMarkFailed()
    {
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob());
        RecordingCompletionRepository completion = new()
        {
            Exception = new OperationCanceledException()
        };
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleAsync(CancellationToken.None));

        Assert.Equal(1, completion.InvocationCount);
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Fact]
    public async Task HandleAsync_AtomicCompletionRejected_ThrowsWithoutFailing()
    {
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob());
        RecordingCompletionRepository completion = new(null);
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(CancellationToken.None));

        Assert.Contains(
            "atomic completion was rejected",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, completion.InvocationCount);
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Theory]
    [InlineData("MismatchedId")]
    [InlineData("WrongStatus")]
    [InlineData("NullSimulationId")]
    [InlineData("EmptySimulationId")]
    [InlineData("NullFinishedAt")]
    [InlineData("FailureMessage")]
    public async Task HandleAsync_MalformedTerminalResult_ThrowsWithoutFailing(
        string malformedCase)
    {
        SimulationJobResult terminal =
            CreateCompletedJob(JobId, SimulationId);
        terminal = malformedCase switch
        {
            "MismatchedId" => terminal with { Id = Guid.NewGuid() },
            "WrongStatus" => terminal with
            {
                Status = SimulationJobStatus.Running
            },
            "NullSimulationId" => terminal with { SimulationId = null },
            "EmptySimulationId" => terminal with
            {
                SimulationId = Guid.Empty
            },
            "NullFinishedAt" => terminal with { FinishedAtUtc = null },
            "FailureMessage" => terminal with
            {
                FailureMessage = "unexpected"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(malformedCase))
        };
        RecordingSimulationJobRepository jobs = new(
            CreateRunningJob());
        RecordingCompletionRepository completion = new(terminal);
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(CancellationToken.None));

        Assert.Equal(1, completion.InvocationCount);
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Theory]
    [InlineData("EmptyId")]
    [InlineData("WrongStatus")]
    [InlineData("NullClaimedAt")]
    [InlineData("SimulationId")]
    [InlineData("FinishedAt")]
    [InlineData("FailureMessage")]
    [InlineData("NullRequest")]
    public async Task HandleAsync_InvalidClaimedSnapshot_ThrowsBeforeProcessing(
        string invalidCase)
    {
        SimulationJobResult claimed = CreateRunningJob(
            CreateRequest(samplingMode: (SamplingMode)999));
        claimed = invalidCase switch
        {
            "EmptyId" => claimed with { Id = Guid.Empty },
            "WrongStatus" => claimed with
            {
                Status = SimulationJobStatus.Pending
            },
            "NullClaimedAt" => claimed with { ClaimedAtUtc = null },
            "SimulationId" => claimed with { SimulationId = SimulationId },
            "FinishedAt" => claimed with { FinishedAtUtc = Timestamp },
            "FailureMessage" => claimed with
            {
                FailureMessage = "unexpected"
            },
            "NullRequest" => claimed with { Request = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
        };
        RecordingSimulationJobRepository jobs = new(claimed);
        RecordingCompletionRepository completion = new();
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(CancellationToken.None));

        Assert.Equal(0, completion.InvocationCount);
        Assert.Equal(0, jobs.FailureInvocationCount);
    }

    [Fact]
    public async Task HandleAsync_ClaimThrowsCancellation_PropagatesWithoutFailureTransition()
    {
        OperationCanceledException cancellationException = new();
        RecordingSimulationJobRepository jobs = new(null)
        {
            ClaimException = cancellationException
        };
        RecordingCompletionRepository completion = new();
        ProcessNextSimulationJobHandler handler = CreateHandler(
            jobs,
            completion);

        OperationCanceledException observed =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => handler.HandleAsync(CancellationToken.None));

        Assert.Same(cancellationException, observed);
        Assert.Equal(0, jobs.FailureInvocationCount);
        Assert.Equal(0, completion.InvocationCount);
    }

    private static ProcessNextSimulationJobHandler CreateHandler(
        RecordingSimulationJobRepository jobs,
        RecordingCompletionRepository completion)
    {
        return new ProcessNextSimulationJobHandler(
            new ClaimSimulationJobHandler(jobs),
            new RunSimulationHandler(),
            completion,
            new FailSimulationJobHandler(jobs));
    }

    private static RunSimulationRequest CreateRequest(
        SamplingMode samplingMode = SamplingMode.Uniform)
    {
        return new RunSimulationRequest(
            SingularTime: 2.75,
            ConcentrationExponent: 0.004,
            StartTime: 0.25,
            EndTime: 1.75,
            SampleCount: 4,
            SamplingMode: samplingMode);
    }

    private static SimulationJobResult CreateRunningJob(
        RunSimulationRequest? request = null)
    {
        return new SimulationJobResult(
            Id: JobId,
            Request: request ?? CreateRequest(),
            Status: SimulationJobStatus.Running,
            CreatedAtUtc: Timestamp.AddMinutes(-1),
            ClaimedAtUtc: Timestamp,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);
    }

    private static SimulationJobResult CreateCompletedJob(
        Guid jobId,
        Guid simulationId)
    {
        return new SimulationJobResult(
            Id: jobId,
            Request: CreateRequest(),
            Status: SimulationJobStatus.Completed,
            CreatedAtUtc: Timestamp.AddMinutes(-1),
            ClaimedAtUtc: Timestamp,
            SimulationId: simulationId,
            FinishedAtUtc: Timestamp.AddSeconds(1),
            FailureMessage: null);
    }

    private sealed class RecordingCompletionRepository :
        ISimulationJobCompletionRepository
    {
        private readonly SimulationJobResult? _result;

        public RecordingCompletionRepository(
            SimulationJobResult? result = null)
        {
            _result = result;
        }

        public Exception? Exception { get; init; }

        public int InvocationCount { get; private set; }

        public Guid ReceivedJobId { get; private set; }

        public RunSimulationResult? ReceivedSimulation { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
            Guid jobId,
            RunSimulationResult simulation,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ReceivedJobId = jobId;
            ReceivedSimulation = simulation;
            ReceivedCancellationToken = cancellationToken;

            return Exception is null
                ? Task.FromResult(_result)
                : Task.FromException<SimulationJobResult?>(Exception);
        }
    }

    private sealed class RecordingSimulationJobRepository :
        ISimulationJobRepository
    {
        private readonly SimulationJobResult? _claimResult;

        public RecordingSimulationJobRepository(
            SimulationJobResult? claimResult)
        {
            _claimResult = claimResult;
        }

        public Exception? ClaimException { get; init; }

        public Exception? FailureException { get; init; }

        public SimulationJobResult? FailureResult { get; init; } =
            CreateCompletedJob(JobId, SimulationId) with
            {
                Status = SimulationJobStatus.Failed,
                SimulationId = null,
                FailureMessage = "Recorded safely."
            };

        public Action? OnClaim { get; set; }

        public int ClaimInvocationCount { get; private set; }

        public int FailureInvocationCount { get; private set; }

        public CancellationToken ClaimCancellationToken { get; private set; }

        public CancellationToken FailureCancellationToken { get; private set; }

        public Guid FailedJobId { get; private set; }

        public string? FailureMessage { get; private set; }

        public Task<SimulationJobResult?> ClaimNextPendingAsync(
            CancellationToken cancellationToken)
        {
            ClaimInvocationCount++;
            ClaimCancellationToken = cancellationToken;
            OnClaim?.Invoke();

            return ClaimException is null
                ? Task.FromResult(_claimResult)
                : Task.FromException<SimulationJobResult?>(ClaimException);
        }

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            FailureInvocationCount++;
            FailedJobId = jobId;
            FailureMessage = failureMessage;
            FailureCancellationToken = cancellationToken;

            return FailureException is null
                ? Task.FromResult(FailureResult)
                : Task.FromException<SimulationJobResult?>(FailureException);
        }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid simulationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}