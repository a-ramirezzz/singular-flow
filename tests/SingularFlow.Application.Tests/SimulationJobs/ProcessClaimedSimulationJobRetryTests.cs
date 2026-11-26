using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class ProcessClaimedSimulationJobRetryTests
{
    private static readonly Guid JobId =
        Guid.Parse("20f268ea-484e-49a4-b8fd-8abb600cc9cb");

    private static readonly Guid ClaimToken =
        Guid.Parse("4c43e6fb-3bd2-4937-812f-94651fdc43bf");

    private static readonly DateTimeOffset ClaimedAt =
        new(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    [InlineData(4, 3)]
    public void Validate_InvalidAttemptMetadata_Throws(
        int attemptCount,
        int maxAttempts)
    {
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                AttemptCount = attemptCount,
                MaxAttempts = maxAttempts
            }
        };

        Assert.Throws<InvalidOperationException>(claim.Validate);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    public void Validate_ValidAttemptBoundaries_Succeeds(
        int attemptCount,
        int maxAttempts)
    {
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                AttemptCount = attemptCount,
                MaxAttempts = maxAttempts
            }
        };

        claim.Validate();
    }

    [Theory]
    [InlineData("NextAttempt")]
    [InlineData("FailedAtOnly")]
    [InlineData("FailureOnly")]
    [InlineData("EmptyFailure")]
    [InlineData("WhitespaceFailure")]
    [InlineData("FailureAfterClaim")]
    public void Validate_InvalidRetryState_Throws(string kind)
    {
        SimulationJobClaim claim = CreateClaim();
        claim = claim with
        {
            Job = kind switch
            {
                "NextAttempt" => claim.Job with
                {
                    NextAttemptAtUtc = ClaimedAt.AddMinutes(1)
                },
                "FailedAtOnly" => claim.Job with
                {
                    LastFailedAtUtc = ClaimedAt.AddMinutes(-1)
                },
                "FailureOnly" => claim.Job with
                {
                    LastFailureMessage = "safe"
                },
                "EmptyFailure" => claim.Job with
                {
                    LastFailedAtUtc = ClaimedAt.AddMinutes(-1),
                    LastFailureMessage = string.Empty
                },
                "WhitespaceFailure" => claim.Job with
                {
                    LastFailedAtUtc = ClaimedAt.AddMinutes(-1),
                    LastFailureMessage = " \t"
                },
                "FailureAfterClaim" => claim.Job with
                {
                    LastFailedAtUtc = ClaimedAt.AddTicks(1),
                    LastFailureMessage = "safe"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            }
        };

        Assert.Throws<InvalidOperationException>(claim.Validate);
    }

    [Fact]
    public void Validate_PairedHistoricalFailureMetadata_Succeeds()
    {
        DateTimeOffset historicalClaim = ClaimedAt.AddYears(-50);
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                ClaimedAtUtc = historicalClaim,
                LastFailedAtUtc = historicalClaim.AddMinutes(-1),
                LastFailureMessage = "safe"
            },
            LeaseExpiresAtUtc = historicalClaim.AddMinutes(5)
        };

        claim.Validate();
    }

    [Fact]
    public async Task HandleAsync_RetryableExecutionFailure_ReturnsScheduled()
    {
        RecordingClassifier classifier = new(
            SimulationJobFailureClassification.Retryable);
        RecordingRetryRepository retries = new()
        {
            Result = new SimulationJobRetryDecision.Scheduled(
                CreateScheduledJob())
        };
        RecordingJobRepository jobs = new();
        using CancellationTokenSource cancellation = new();
        ProcessClaimedSimulationJobHandler handler = new(
            new RunSimulationHandler(),
            new RejectingCompletionRepository(),
            new FailSimulationJobHandler(jobs),
            classifier,
            retries,
            SimulationJobRetryPolicy.Default);

        ProcessClaimedSimulationJobResult result = await handler.HandleAsync(
            CreateClaim() with
            {
                Job = CreateClaim().Job with
                {
                    Request = CreateClaim().Job.Request with
                    {
                        SamplingMode = (SamplingMode)999
                    }
                }
            },
            cancellation.Token);

        ProcessClaimedSimulationJobResult.RetryScheduled scheduled =
            Assert.IsType<ProcessClaimedSimulationJobResult.RetryScheduled>(
                result);
        Assert.Equal(JobId, scheduled.JobId);
        Assert.Equal(ClaimedAt.AddMinutes(1), scheduled.NextAttemptAtUtc);
        Assert.IsType<ArgumentOutOfRangeException>(classifier.Exception);
        Assert.Equal(SimulationJobFailurePhase.Execution, classifier.Phase);
        Assert.Equal(1, classifier.Count);
        Assert.Equal(1, retries.Count);
        Assert.Equal(JobId, retries.JobId);
        Assert.Equal(ClaimToken, retries.ClaimToken);
        Assert.Equal("Simulation execution failed.", retries.Message);
        Assert.Equal(TimeSpan.FromSeconds(5), retries.Delay);
        Assert.Equal(cancellation.Token, retries.Token);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_RetryableExecutionFailure_Exhausted_ReturnsFailed()
    {
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                AttemptCount = 3,
                MaxAttempts = 3,
                Request = CreateClaim().Job.Request with
                {
                    SamplingMode = (SamplingMode)999
                }
            }
        };
        RecordingRetryRepository retries = new()
        {
            Result = new SimulationJobRetryDecision.Exhausted(
                CreateExhaustedJob())
        };

        ProcessClaimedSimulationJobResult result = await CreateHandler(
            new RecordingClassifier(
                SimulationJobFailureClassification.Retryable),
            retries,
            new RecordingJobRepository(),
            new RejectingCompletionRepository()).HandleAsync(
                claim,
                CancellationToken.None);

        ProcessClaimedSimulationJobResult.Failed failed =
            Assert.IsType<ProcessClaimedSimulationJobResult.Failed>(result);
        Assert.Equal(JobId, failed.JobId);
    }

    [Fact]
    public async Task HandleAsync_RetryFencingRejected_ReturnsLeaseLost()
    {
        RecordingJobRepository jobs = new();
        ProcessClaimedSimulationJobResult result = await CreateHandler(
            new RecordingClassifier(
                SimulationJobFailureClassification.Retryable),
            new RecordingRetryRepository(),
            jobs,
            new RejectingCompletionRepository()).HandleAsync(
                CreateExecutionFailureClaim(),
                CancellationToken.None);

        Assert.IsType<ProcessClaimedSimulationJobResult.LeaseLost>(result);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Theory]
    [InlineData("WrongId")]
    [InlineData("WrongStatus")]
    [InlineData("MissingDue")]
    [InlineData("Claimed")]
    [InlineData("Finished")]
    [InlineData("Simulation")]
    [InlineData("TerminalFailure")]
    [InlineData("AttemptChanged")]
    [InlineData("MaximumChanged")]
    [InlineData("Exhausted")]
    [InlineData("MissingFailedAt")]
    [InlineData("MissingFailure")]
    [InlineData("WhitespaceFailure")]
    public async Task HandleAsync_MalformedScheduledDecision_Throws(
        string kind)
    {
        SimulationJobResult job = CreateScheduledJob();
        job = kind switch
        {
            "WrongId" => job with { Id = Guid.NewGuid() },
            "WrongStatus" => job with { Status = SimulationJobStatus.Running },
            "MissingDue" => job with { NextAttemptAtUtc = null },
            "Claimed" => job with { ClaimedAtUtc = ClaimedAt },
            "Finished" => job with { FinishedAtUtc = ClaimedAt },
            "Simulation" => job with { SimulationId = Guid.NewGuid() },
            "TerminalFailure" => job with { FailureMessage = "safe" },
            "AttemptChanged" => job with { AttemptCount = 2 },
            "MaximumChanged" => job with { MaxAttempts = 4 },
            "Exhausted" => job with { AttemptCount = 3 },
            "MissingFailedAt" => job with { LastFailedAtUtc = null },
            "MissingFailure" => job with { LastFailureMessage = null },
            "WhitespaceFailure" => job with { LastFailureMessage = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        RecordingRetryRepository retries = new()
        {
            Result = new SimulationJobRetryDecision.Scheduled(job)
        };
        RecordingJobRepository jobs = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler(
                new RecordingClassifier(
                    SimulationJobFailureClassification.Retryable),
                retries,
                jobs,
                new RejectingCompletionRepository()).HandleAsync(
                    CreateExecutionFailureClaim(),
                    CancellationToken.None));

        Assert.Equal(1, retries.Count);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Theory]
    [InlineData("WrongId")]
    [InlineData("WrongStatus")]
    [InlineData("MissingClaimed")]
    [InlineData("MissingFinished")]
    [InlineData("Simulation")]
    [InlineData("MissingTerminalFailure")]
    [InlineData("WhitespaceTerminalFailure")]
    [InlineData("Due")]
    [InlineData("AttemptChanged")]
    [InlineData("MaximumChanged")]
    [InlineData("NotExhausted")]
    [InlineData("MissingFailedAt")]
    [InlineData("MissingFailure")]
    [InlineData("WhitespaceFailure")]
    public async Task HandleAsync_MalformedExhaustedDecision_Throws(
        string kind)
    {
        SimulationJobResult job = CreateExhaustedJob();
        job = kind switch
        {
            "WrongId" => job with { Id = Guid.NewGuid() },
            "WrongStatus" => job with { Status = SimulationJobStatus.Pending },
            "MissingClaimed" => job with { ClaimedAtUtc = null },
            "MissingFinished" => job with { FinishedAtUtc = null },
            "Simulation" => job with { SimulationId = Guid.NewGuid() },
            "MissingTerminalFailure" => job with { FailureMessage = null },
            "WhitespaceTerminalFailure" => job with { FailureMessage = " " },
            "Due" => job with { NextAttemptAtUtc = ClaimedAt },
            "AttemptChanged" => job with { AttemptCount = 2 },
            "MaximumChanged" => job with { MaxAttempts = 2 },
            "NotExhausted" => job with { MaxAttempts = 4 },
            "MissingFailedAt" => job with { LastFailedAtUtc = null },
            "MissingFailure" => job with { LastFailureMessage = null },
            "WhitespaceFailure" => job with { LastFailureMessage = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                AttemptCount = 3,
                MaxAttempts = 3,
                Request = CreateClaim().Job.Request with
                {
                    SamplingMode = (SamplingMode)999
                }
            }
        };
        RecordingRetryRepository retries = new()
        {
            Result = new SimulationJobRetryDecision.Exhausted(job)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler(
                new RecordingClassifier(
                    SimulationJobFailureClassification.Retryable),
                retries,
                new RecordingJobRepository(),
                new RejectingCompletionRepository()).HandleAsync(
                    claim,
                    CancellationToken.None));

        Assert.Equal(1, retries.Count);
    }

    [Fact]
    public async Task HandleAsync_RetryablePersistenceFailure_ClassifiesExactException()
    {
        InvalidOperationException primary = new("sensitive persistence");
        RecordingClassifier classifier = new(
            SimulationJobFailureClassification.Retryable);
        RecordingRetryRepository retries = new();

        ProcessClaimedSimulationJobResult result = await CreateHandler(
            classifier,
            retries,
            new RecordingJobRepository(),
            new ThrowingCompletionRepository(primary)).HandleAsync(
                CreateClaim(),
                CancellationToken.None);

        Assert.IsType<ProcessClaimedSimulationJobResult.LeaseLost>(result);
        Assert.Same(primary, classifier.Exception);
        Assert.Equal(SimulationJobFailurePhase.Persistence, classifier.Phase);
        Assert.Equal(
            "Simulation persistence or completion failed.",
            retries.Message);
    }

    [Fact]
    public async Task HandleAsync_PersistedAttemptAboveConfiguredMaximum_SchedulesRetry()
    {
        InvalidOperationException primary = new("sensitive persistence");
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                AttemptCount = 4,
                MaxAttempts = 10
            }
        };
        SimulationJobResult persistedJob = claim.Job with
        {
            Status = SimulationJobStatus.Pending,
            ClaimedAtUtc = null,
            NextAttemptAtUtc = ClaimedAt.AddMinutes(2),
            LastFailedAtUtc = ClaimedAt.AddSeconds(1),
            LastFailureMessage = "Simulation persistence or completion failed."
        };
        RecordingRetryRepository retries = new()
        {
            Result = new SimulationJobRetryDecision.Scheduled(persistedJob)
        };
        RecordingJobRepository jobs = new();
        using CancellationTokenSource cancellation = new();
        ProcessClaimedSimulationJobHandler handler = new(
            new RunSimulationHandler(),
            new ThrowingCompletionRepository(primary),
            new FailSimulationJobHandler(jobs),
            new RecordingClassifier(
                SimulationJobFailureClassification.Retryable),
            retries,
            new SimulationJobRetryPolicy(
                maxAttempts: 3,
                baseDelay: TimeSpan.FromSeconds(3),
                maximumDelay: TimeSpan.FromSeconds(30)));

        ProcessClaimedSimulationJobResult result = await handler.HandleAsync(
            claim,
            cancellation.Token);

        ProcessClaimedSimulationJobResult.RetryScheduled scheduled =
            Assert.IsType<ProcessClaimedSimulationJobResult.RetryScheduled>(
                result);
        Assert.Equal(JobId, scheduled.JobId);
        Assert.Equal(persistedJob.NextAttemptAtUtc, scheduled.NextAttemptAtUtc);
        Assert.Equal(4, persistedJob.AttemptCount);
        Assert.Equal(10, persistedJob.MaxAttempts);
        Assert.Equal(1, retries.Count);
        Assert.Equal(JobId, retries.JobId);
        Assert.Equal(ClaimToken, retries.ClaimToken);
        Assert.Equal(
            "Simulation persistence or completion failed.",
            retries.Message);
        Assert.Equal(TimeSpan.FromSeconds(24), retries.Delay);
        Assert.Equal(cancellation.Token, retries.Token);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_PersistedMaximumAttempt_MapsFencedExhaustion()
    {
        InvalidOperationException primary = new("sensitive persistence");
        SimulationJobClaim claim = CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                AttemptCount = 10,
                MaxAttempts = 10
            }
        };
        SimulationJobResult persistedJob = claim.Job with
        {
            Status = SimulationJobStatus.Failed,
            FinishedAtUtc = ClaimedAt.AddMinutes(2),
            FailureMessage = "Simulation persistence or completion failed.",
            LastFailedAtUtc = ClaimedAt.AddSeconds(1),
            LastFailureMessage = "Simulation persistence or completion failed."
        };
        RecordingRetryRepository retries = new()
        {
            Result = new SimulationJobRetryDecision.Exhausted(persistedJob)
        };
        RecordingJobRepository jobs = new();
        using CancellationTokenSource cancellation = new();
        ProcessClaimedSimulationJobHandler handler = new(
            new RunSimulationHandler(),
            new ThrowingCompletionRepository(primary),
            new FailSimulationJobHandler(jobs),
            new RecordingClassifier(
                SimulationJobFailureClassification.Retryable),
            retries,
            new SimulationJobRetryPolicy(
                maxAttempts: 3,
                baseDelay: TimeSpan.FromSeconds(3),
                maximumDelay: TimeSpan.FromSeconds(30)));

        ProcessClaimedSimulationJobResult result = await handler.HandleAsync(
            claim,
            cancellation.Token);

        ProcessClaimedSimulationJobResult.Failed failed =
            Assert.IsType<ProcessClaimedSimulationJobResult.Failed>(result);
        Assert.Equal(JobId, failed.JobId);
        Assert.Equal(1, retries.Count);
        Assert.Equal(JobId, retries.JobId);
        Assert.Equal(ClaimToken, retries.ClaimToken);
        Assert.Equal(
            "Simulation persistence or completion failed.",
            retries.Message);
        Assert.Equal(TimeSpan.FromSeconds(30), retries.Delay);
        Assert.Equal(cancellation.Token, retries.Token);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_ClassifierFailure_PreservesPrimaryAndFailsOnce()
    {
        InvalidOperationException classifierFailure = new("classifier");
        RecordingClassifier classifier = new(
            SimulationJobFailureClassification.Retryable)
        {
            ExceptionToThrow = classifierFailure
        };
        RecordingJobRepository jobs = new();

        ArgumentOutOfRangeException observed =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                CreateHandler(
                    classifier,
                    new RecordingRetryRepository(),
                    jobs,
                    new RejectingCompletionRepository()).HandleAsync(
                        CreateExecutionFailureClaim(),
                        CancellationToken.None));

        Assert.Contains(nameof(RunSimulationHandler.Handle), observed.StackTrace);
        Assert.Equal(1, jobs.FencedFailureCount);
    }

    [Fact]
    public async Task HandleAsync_RetryRepositoryFailure_PreservesPrimaryWithoutTerminalFallback()
    {
        IOException secondary = new("retry persistence");
        RecordingRetryRepository retries = new()
        {
            Exception = secondary
        };
        RecordingJobRepository jobs = new();

        ArgumentOutOfRangeException observed =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                CreateHandler(
                    new RecordingClassifier(
                        SimulationJobFailureClassification.Retryable),
                    retries,
                    jobs,
                    new RejectingCompletionRepository()).HandleAsync(
                        CreateExecutionFailureClaim(),
                        CancellationToken.None));

        Assert.Contains(nameof(RunSimulationHandler.Handle), observed.StackTrace);
        Assert.Equal(1, retries.Count);
        Assert.Equal(0, jobs.FencedFailureCount);
    }

    private static SimulationJobClaim CreateClaim()
    {
        SimulationJobResult job = new(
            JobId,
            new RunSimulationRequest(
                2.0,
                0.005,
                0.0,
                1.0,
                3,
                SamplingMode.Uniform),
            SimulationJobStatus.Running,
            ClaimedAt.AddMinutes(-1),
            ClaimedAt,
            null,
            null,
            null)
        {
            AttemptCount = 1,
            MaxAttempts = 3
        };

        return new SimulationJobClaim(
            job,
            ClaimToken,
            ClaimedAt.AddMinutes(5));
    }

    private static SimulationJobResult CreateScheduledJob() =>
        CreateClaim().Job with
        {
            Status = SimulationJobStatus.Pending,
            ClaimedAtUtc = null,
            NextAttemptAtUtc = ClaimedAt.AddMinutes(1),
            LastFailedAtUtc = ClaimedAt.AddSeconds(1),
            LastFailureMessage = "Simulation execution failed."
        };

    private static SimulationJobResult CreateExhaustedJob() =>
        CreateClaim().Job with
        {
            Status = SimulationJobStatus.Failed,
            AttemptCount = 3,
            MaxAttempts = 3,
            FinishedAtUtc = ClaimedAt.AddMinutes(1),
            FailureMessage = "Simulation execution failed.",
            LastFailedAtUtc = ClaimedAt.AddSeconds(1),
            LastFailureMessage = "Simulation execution failed."
        };

    private static SimulationJobClaim CreateExecutionFailureClaim() =>
        CreateClaim() with
        {
            Job = CreateClaim().Job with
            {
                Request = CreateClaim().Job.Request with
                {
                    SamplingMode = (SamplingMode)999
                }
            }
        };

    private static ProcessClaimedSimulationJobHandler CreateHandler(
        ISimulationJobFailureClassifier classifier,
        ISimulationJobRetryRepository retries,
        RecordingJobRepository jobs,
        ISimulationJobCompletionRepository completion) =>
        new(
            new RunSimulationHandler(),
            completion,
            new FailSimulationJobHandler(jobs),
            classifier,
            retries,
            SimulationJobRetryPolicy.Default);

    private sealed class RecordingClassifier :
        ISimulationJobFailureClassifier
    {
        private readonly SimulationJobFailureClassification _result;

        public RecordingClassifier(
            SimulationJobFailureClassification result)
        {
            _result = result;
        }

        public int Count { get; private set; }
        public Exception? Exception { get; private set; }
        public SimulationJobFailurePhase Phase { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public SimulationJobFailureClassification Classify(
            Exception exception,
            SimulationJobFailurePhase phase)
        {
            Count++;
            Exception = exception;
            Phase = phase;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return _result;
        }
    }

    private sealed class RecordingRetryRepository :
        ISimulationJobRetryRepository
    {
        public SimulationJobRetryDecision? Result { get; init; }
        public Exception? Exception { get; init; }
        public int Count { get; private set; }
        public Guid JobId { get; private set; }
        public Guid ClaimToken { get; private set; }
        public string? Message { get; private set; }
        public TimeSpan Delay { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<SimulationJobRetryDecision?>
            TryRecordRetryableFailureAsync(
                Guid jobId,
                Guid claimToken,
                string safeFailureMessage,
                TimeSpan retryDelay,
                CancellationToken cancellationToken)
        {
            Count++;
            JobId = jobId;
            ClaimToken = claimToken;
            Message = safeFailureMessage;
            Delay = retryDelay;
            Token = cancellationToken;
            if (Exception is not null)
            {
                return Task.FromException<SimulationJobRetryDecision?>(
                    Exception);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingCompletionRepository :
        ISimulationJobCompletionRepository
    {
        private readonly Exception _exception;

        public ThrowingCompletionRepository(Exception exception)
        {
            _exception = exception;
        }

        public Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
            Guid jobId,
            Guid claimToken,
            RunSimulationResult simulation,
            CancellationToken cancellationToken) =>
            Task.FromException<SimulationJobResult?>(_exception);
    }

    private sealed class RejectingCompletionRepository :
        ISimulationJobCompletionRepository
    {
        public Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
            Guid jobId,
            Guid claimToken,
            RunSimulationResult simulation,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Completion was not expected.");
    }

    private sealed class RecordingJobRepository : ISimulationJobRepository
    {
        public int FencedFailureCount { get; private set; }

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            Guid claimToken,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            FencedFailureCount++;
            return Task.FromResult<SimulationJobResult?>(null);
        }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            int maxAttempts,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid claimToken,
            Guid simulationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}