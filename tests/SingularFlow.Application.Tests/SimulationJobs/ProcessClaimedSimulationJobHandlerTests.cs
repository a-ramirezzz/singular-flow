using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class ProcessClaimedSimulationJobHandlerTests
{
    private static readonly Guid JobId = Guid.Parse("13245308-f45f-4b77-b792-bf3375f2ff7c");
    private static readonly Guid ClaimToken = Guid.Parse("34d735e9-2e87-46ee-b9f2-82230300842b");
    private static readonly Guid SimulationId = Guid.Parse("a3fa230a-8e14-4ff8-b1ac-ff8d3335bc6c");
    private static readonly DateTimeOffset ClaimedAt = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidClaim_CompletesThroughFencedOperation()
    {
        RunSimulationRequest request = CreateRequest();
        SimulationJobClaim claim = CreateClaim(request);
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new(CreateCompletedJob());
        ProcessClaimedSimulationJobHandler handler = CreateHandler(jobs, completion);
        using CancellationTokenSource cancellation = new();

        ProcessClaimedSimulationJobResult result = await handler.HandleAsync(claim, cancellation.Token);

        ProcessClaimedSimulationJobResult.Completed completed =
            Assert.IsType<ProcessClaimedSimulationJobResult.Completed>(result);
        Assert.Equal(JobId, completed.JobId);
        Assert.Equal(SimulationId, completed.SimulationId);
        Assert.Equal(1, completion.FencedCount);
        Assert.Equal(JobId, completion.JobId);
        Assert.Equal(ClaimToken, completion.ClaimToken);
        Assert.Equal(cancellation.Token, completion.Token);
        RunSimulationResult simulation = Assert.IsType<RunSimulationResult>(completion.Simulation);
        Assert.Equal(request.SingularTime, simulation.BlowupParameters.SingularTime);
        Assert.Equal(request.ConcentrationExponent, simulation.BlowupParameters.ConcentrationExponent);
        Assert.Equal(request.StartTime, simulation.TimeSeriesParameters.StartTime);
        Assert.Equal(request.EndTime, simulation.TimeSeriesParameters.EndTime);
        Assert.Equal(request.SampleCount, simulation.TimeSeriesParameters.SampleCount);
        Assert.Equal(new[] { 0.25, 0.75, 1.25, 1.75 }, simulation.States.Select(state => state.Time));
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_FencedCompletionRejected_ReturnsLeaseLost()
    {
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new(null);
        ProcessClaimedSimulationJobHandler handler = CreateHandler(jobs, completion);

        ProcessClaimedSimulationJobResult result =
            await handler.HandleAsync(CreateClaim(), CancellationToken.None);

        ProcessClaimedSimulationJobResult.LeaseLost leaseLost =
            Assert.IsType<ProcessClaimedSimulationJobResult.LeaseLost>(result);
        Assert.Equal(JobId, leaseLost.JobId);
        Assert.Equal(1, completion.FencedCount);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_LocallyExpiredStructuralLease_StillCompletes()
    {
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with { ClaimedAtUtc = ClaimedAt.AddYears(-10) },
            LeaseExpiresAtUtc = ClaimedAt.AddYears(-10).AddMinutes(1)
        };
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new(CreateCompletedJob());

        ProcessClaimedSimulationJobResult result = await CreateHandler(jobs, completion)
            .HandleAsync(claim, CancellationToken.None);

        Assert.IsType<ProcessClaimedSimulationJobResult.Completed>(result);
        Assert.Equal(1, completion.FencedCount);
    }

    [Fact]
    public async Task HandleAsync_CalculationFailure_RecordsFencedFailureAndRethrows()
    {
        SimulationJobClaim claim = CreateClaim(CreateRequest((SamplingMode)999));
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new();
        ProcessClaimedSimulationJobHandler handler = CreateHandler(jobs, completion);

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(claim, CancellationToken.None));

        Assert.Contains(nameof(RunSimulationHandler.Handle), exception.StackTrace);
        Assert.Equal(1, jobs.FencedFailureCount);
        Assert.Equal(JobId, jobs.JobId);
        Assert.Equal(ClaimToken, jobs.ClaimToken);
        Assert.Equal("Simulation execution failed.", jobs.Message);
        Assert.Equal(0, completion.FencedCount);
    }

    [Theory]
    [InlineData("returns-null")]
    [InlineData("throws")]
    [InlineData("cancels")]
    public async Task HandleAsync_SecondaryFailureProblem_PreservesOriginal(string mode)
    {
        InvalidOperationException original = new("sensitive persistence details");
        Exception? secondary = mode switch
        {
            "throws" => new IOException("secondary details"),
            "cancels" => new OperationCanceledException("secondary cancellation"),
            _ => null
        };
        RecordingJobRepository jobs = new()
        {
            FailureResult = mode == "returns-null" ? null : CreateFailedJob(),
            FailureException = secondary
        };
        RecordingCompletionRepository completion = new() { Exception = original };

        InvalidOperationException observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateHandler(jobs, completion).HandleAsync(CreateClaim(), CancellationToken.None));

        Assert.Same(original, observed);
        Assert.Single(observed.Data.Values.Cast<object>());
        if (secondary is null)
        {
            Assert.Contains(observed.Data.Values.Cast<object>(), value =>
                value is string text && text.Contains("transition returned no result", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(observed.Data.Values.Cast<object>(), value => ReferenceEquals(value, secondary));
        }
    }

    [Fact]
    public async Task HandleAsync_CompletionFailure_RecordsSafeFencedFailureAndRethrowsOriginal()
    {
        InvalidOperationException original = new("sensitive persistence details");
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new() { Exception = original };
        using CancellationTokenSource cancellation = new();

        InvalidOperationException observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateHandler(jobs, completion).HandleAsync(CreateClaim(), cancellation.Token));

        Assert.Same(original, observed);
        Assert.Equal("Simulation persistence or completion failed.", jobs.Message);
        Assert.Equal(JobId, jobs.JobId);
        Assert.Equal(ClaimToken, jobs.ClaimToken);
        Assert.Equal(cancellation.Token, jobs.Token);
        Assert.Equal(1, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_PreCancelled_StopsBeforeProcessing()
    {
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateHandler(jobs, completion).HandleAsync(CreateClaim(), cancellation.Token));

        Assert.Equal(0, completion.FencedCount);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_CompletionCancellation_DoesNotRecordFailure()
    {
        OperationCanceledException original = new();
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new() { Exception = original };

        OperationCanceledException observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateHandler(jobs, completion).HandleAsync(CreateClaim(), CancellationToken.None));

        Assert.Same(original, observed);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Theory]
    [InlineData("MismatchedId")]
    [InlineData("WrongStatus")]
    [InlineData("NullSimulationId")]
    [InlineData("EmptySimulationId")]
    [InlineData("NullFinishedAt")]
    [InlineData("FailureMessage")]
    [InlineData("NullClaimedAt")]
    public async Task HandleAsync_MalformedCompletedSnapshot_ThrowsWithoutFailure(string kind)
    {
        SimulationJobResult job = CreateCompletedJob();
        job = kind switch
        {
            "MismatchedId" => job with { Id = Guid.NewGuid() },
            "WrongStatus" => job with { Status = SimulationJobStatus.Running },
            "NullSimulationId" => job with { SimulationId = null },
            "EmptySimulationId" => job with { SimulationId = Guid.Empty },
            "NullFinishedAt" => job with { FinishedAtUtc = null },
            "FailureMessage" => job with { FailureMessage = "unexpected" },
            "NullClaimedAt" => job with { ClaimedAtUtc = null },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        RecordingJobRepository jobs = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateHandler(
            jobs, new RecordingCompletionRepository(job)).HandleAsync(CreateClaim(), CancellationToken.None));

        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Theory]
    [InlineData("NullJob")]
    [InlineData("EmptyId")]
    [InlineData("WrongStatus")]
    [InlineData("NullClaimedAt")]
    [InlineData("EmptyToken")]
    [InlineData("EqualLease")]
    [InlineData("EarlierLease")]
    [InlineData("SimulationId")]
    [InlineData("FinishedAt")]
    [InlineData("FailureMessage")]
    [InlineData("NullRequest")]
    public async Task HandleAsync_InvalidClaim_ThrowsBeforeProcessing(string kind)
    {
        SimulationJobClaim claim = CreateClaim(CreateRequest((SamplingMode)999));
        claim = kind switch
        {
            "NullJob" => claim with { Job = null! },
            "EmptyId" => claim with { Job = claim.Job with { Id = Guid.Empty } },
            "WrongStatus" => claim with { Job = claim.Job with { Status = SimulationJobStatus.Pending } },
            "NullClaimedAt" => claim with { Job = claim.Job with { ClaimedAtUtc = null } },
            "EmptyToken" => claim with { ClaimToken = Guid.Empty },
            "EqualLease" => claim with { LeaseExpiresAtUtc = ClaimedAt },
            "EarlierLease" => claim with { LeaseExpiresAtUtc = ClaimedAt.AddTicks(-1) },
            "SimulationId" => claim with { Job = claim.Job with { SimulationId = SimulationId } },
            "FinishedAt" => claim with { Job = claim.Job with { FinishedAtUtc = ClaimedAt } },
            "FailureMessage" => claim with { Job = claim.Job with { FailureMessage = "unexpected" } },
            "NullRequest" => claim with { Job = claim.Job with { Request = null! } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler(jobs, completion).HandleAsync(claim, CancellationToken.None));

        Assert.Equal(0, completion.FencedCount);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_NullClaim_ThrowsNamedArgumentBeforeProcessing()
    {
        RecordingJobRepository jobs = new();
        RecordingCompletionRepository completion = new();

        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateHandler(jobs, completion).HandleAsync(null!, CancellationToken.None));

        Assert.Equal("claim", exception.ParamName);
        Assert.Equal(0, completion.FencedCount);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    private static ProcessClaimedSimulationJobHandler CreateHandler(
        RecordingJobRepository jobs, RecordingCompletionRepository completion) =>
        new(new RunSimulationHandler(), completion, new FailSimulationJobHandler(jobs));

    private static RunSimulationRequest CreateRequest(SamplingMode mode = SamplingMode.Uniform) =>
        new(2.75, 0.004, 0.25, 1.75, 4, mode);

    private static SimulationJobClaim CreateClaim(RunSimulationRequest? request = null) =>
        new(new SimulationJobResult(JobId, request ?? CreateRequest(), SimulationJobStatus.Running,
            ClaimedAt.AddMinutes(-1), ClaimedAt, null, null, null),
            ClaimToken, ClaimedAt.AddMinutes(5));

    private static SimulationJobResult CreateCompletedJob() =>
        CreateClaim().Job with { Status = SimulationJobStatus.Completed, SimulationId = SimulationId, FinishedAtUtc = ClaimedAt.AddSeconds(1) };

    private static SimulationJobResult CreateFailedJob() =>
        CreateClaim().Job with { Status = SimulationJobStatus.Failed, FinishedAtUtc = ClaimedAt.AddSeconds(1), FailureMessage = "safe" };

    private sealed class RecordingCompletionRepository : ISimulationJobCompletionRepository
    {
        private readonly SimulationJobResult? _result;
        public RecordingCompletionRepository(SimulationJobResult? result = null) => _result = result;
        public Exception? Exception { get; init; }
        public int FencedCount { get; private set; }
        public Guid JobId { get; private set; }
        public Guid ClaimToken { get; private set; }
        public RunSimulationResult? Simulation { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(Guid jobId, Guid claimToken, RunSimulationResult simulation, CancellationToken cancellationToken)
        {
            FencedCount++; JobId = jobId; ClaimToken = claimToken; Simulation = simulation; Token = cancellationToken;
            return Exception is null ? Task.FromResult(_result) : Task.FromException<SimulationJobResult?>(Exception);
        }
    }

    private sealed class RecordingJobRepository : ISimulationJobRepository
    {
        public SimulationJobResult? FailureResult { get; init; } = CreateFailedJob();
        public Exception? FailureException { get; init; }
        public int FencedFailureCount { get; private set; }
        public Guid JobId { get; private set; }
        public Guid ClaimToken { get; private set; }
        public string? Message { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<SimulationJobResult?> TryFailAsync(Guid jobId, Guid claimToken, string failureMessage, CancellationToken cancellationToken)
        {
            FencedFailureCount++; JobId = jobId; ClaimToken = claimToken; Message = failureMessage; Token = cancellationToken;
            return FailureException is null ? Task.FromResult(FailureResult) : Task.FromException<SimulationJobResult?>(FailureException);
        }
        public Task<SimulationJobResult> CreateAsync(RunSimulationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> TryCompleteAsync(Guid jobId, Guid claimToken, Guid simulationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}