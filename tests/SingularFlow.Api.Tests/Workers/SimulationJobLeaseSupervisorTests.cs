using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SingularFlow.Api.Workers;
using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Workers;

public sealed class SimulationJobLeaseSupervisorTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidInput_FailsBeforeScopeOrDelay(
        SimulationJobClaim? claim,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        string parameterName)
    {
        ControlledDelay delay = new();
        RecordingScopeFactory scopes = new(_ => throw new UnreachableException());
        SimulationJobLeaseSupervisor supervisor = CreateSupervisor(scopes, delay);

        ArgumentException exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => supervisor.ProcessAsync(
                claim!, leaseDuration, heartbeatInterval, default));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(0, scopes.CreateCount);
        Assert.Empty(delay.Requests);
    }

    [Fact]
    public async Task PreCancelledToken_CreatesNoScopeOrDelay()
    {
        ControlledDelay delay = new();
        RecordingScopeFactory scopes = new(_ => throw new UnreachableException());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateSupervisor(scopes, delay).ProcessAsync(
                CreateClaim(), LeaseDuration, HeartbeatInterval, cancellation.Token));

        Assert.Equal(0, scopes.CreateCount);
        Assert.Empty(delay.Requests);
    }

    [Theory]
    [MemberData(nameof(StructurallyInvalidClaims))]
    public async Task StructurallyInvalidClaim_FailsBeforeResourcesOrSideEffects(
        SimulationJobClaim claim,
        string expectedMessage)
    {
        ControlledDelay delay = new();
        ControlledCompletionRepository completion = new();
        ControlledJobRepository jobs = new();
        RecordingScope processing = ProcessingScope(completion, jobs: jobs);
        RecordingScopeFactory scopes = new(_ => processing);
        RecordingLogger<SimulationJobLeaseSupervisor> logger = new();

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateSupervisor(scopes, delay, logger).ProcessAsync(
                    claim,
                    LeaseDuration,
                    HeartbeatInterval,
                    default));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Equal(0, scopes.CreateCount);
        Assert.Empty(scopes.Scopes);
        Assert.Equal(0, processing.Provider.ProcessingResolutions);
        Assert.Equal(0, processing.Provider.RenewalResolutions);
        Assert.Empty(delay.Requests);
        Assert.Equal(0, completion.FencedCount);
        Assert.Equal(0, jobs.FencedFailureCount);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessingCompletesBeforeHeartbeat_PreservesResultAndDisposes(
        bool completed)
    {
        SimulationJobClaim claim = CreateClaim();
        ProcessClaimedSimulationJobResult expected = completed
            ? new ProcessClaimedSimulationJobResult.Completed(claim.Job.Id, Guid.NewGuid())
            : new ProcessClaimedSimulationJobResult.LeaseLost(claim.Job.Id);
        ControlledCompletionRepository completion = new(expected);
        RecordingScope processing = ProcessingScope(completion);
        RecordingScopeFactory scopes = new(_ => processing);
        ControlledDelay delay = new();

        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay)
            .ProcessAsync(claim, LeaseDuration, HeartbeatInterval, default);
        DelayRequest heartbeat = await delay.ReadAsync();
        ProcessClaimedSimulationJobResult result = await task;

        Assert.Equal(expected, result);
        Assert.Equal(1, scopes.CreateCount);
        Assert.Equal(1, processing.Provider.ProcessingResolutions);
        Assert.Equal(1, processing.AsyncDisposeCount);
        Assert.True(completion.ReceivedToken.CanBeCanceled);
        Assert.Equal(claim, completion.ReceivedClaim);
        Assert.True(heartbeat.Token.IsCancellationRequested);
        Assert.Equal(HeartbeatInterval, heartbeat.Duration);
    }

    [Fact]
    public async Task SuccessfulRenewals_UseFreshSequentialScopesAndExactValues()
    {
        SimulationJobClaim claim = CreateClaim();
        ControlledCompletionRepository completion = new();
        RecordingScope processing = ProcessingScope(completion);
        ControlledLeaseRepository firstLease = new(DateTimeOffset.Parse("2030-01-01T00:02:00Z"));
        ControlledLeaseRepository secondLease = new(DateTimeOffset.Parse("2030-01-01T00:03:00Z"));
        RecordingScope firstRenewal = RenewalScope(firstLease);
        RecordingScope secondRenewal = RenewalScope(secondLease);
        RecordingScopeFactory scopes = new(index => index switch
        {
            0 => processing,
            1 => firstRenewal,
            2 => secondRenewal,
            _ => throw new InvalidOperationException("Unexpected scope.")
        });
        ControlledDelay delay = new();
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay)
            .ProcessAsync(claim, LeaseDuration, HeartbeatInterval, default);

        DelayRequest firstWait = await delay.ReadAsync();
        Assert.Equal(1, scopes.CreateCount);
        firstWait.Release();
        await firstLease.WaitForInvocationAsync();
        DelayRequest secondWait = await delay.ReadAsync();

        Assert.Equal(2, scopes.CreateCount);
        Assert.Equal(1, firstRenewal.AsyncDisposeCount);
        Assert.Equal(HeartbeatInterval, firstWait.Duration);
        Assert.True(firstWait.Token.CanBeCanceled);
        Assert.Equal(claim.Job.Id, firstLease.JobId);
        Assert.Equal(claim.ClaimToken, firstLease.ClaimToken);
        Assert.Equal(LeaseDuration, firstLease.LeaseDuration);
        Assert.Equal(firstWait.Token, firstLease.Token);

        secondWait.Release();
        await secondLease.WaitForInvocationAsync();
        _ = await delay.ReadAsync();
        ProcessClaimedSimulationJobResult.Completed expected =
            new(claim.Job.Id, Guid.NewGuid());
        completion.Complete(expected);

        Assert.Equal(expected, await task);
        Assert.Equal(3, scopes.CreateCount);
        Assert.NotSame(firstRenewal, secondRenewal);
        Assert.Equal(1, secondRenewal.AsyncDisposeCount);
        Assert.Equal(1, processing.AsyncDisposeCount);
    }

    [Fact]
    public async Task RenewalLoss_CancelsProcessingAndReturnsLeaseLost()
    {
        SimulationJobClaim claim = CreateClaim();
        ControlledCompletionRepository completion = new();
        ControlledLeaseRepository lease = new(null);
        RecordingScope processing = ProcessingScope(completion);
        RecordingScope renewal = RenewalScope(lease);
        RecordingScopeFactory scopes = new(index => index == 0 ? processing : renewal);
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobLeaseSupervisor> logger = new();
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay, logger)
            .ProcessAsync(claim, LeaseDuration, HeartbeatInterval, default);

        (await delay.ReadAsync()).Release();
        ProcessClaimedSimulationJobResult.LeaseLost result =
            Assert.IsType<ProcessClaimedSimulationJobResult.LeaseLost>(await task);

        Assert.Equal(claim.Job.Id, result.JobId);
        Assert.True(completion.ReceivedToken.IsCancellationRequested);
        Assert.Equal(2, scopes.CreateCount);
        Assert.Equal(1, processing.AsyncDisposeCount);
        Assert.Equal(1, renewal.AsyncDisposeCount);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task CompletionConcurrentWithRenewalLoss_CompletedWins()
    {
        SimulationJobClaim claim = CreateClaim();
        ControlledCompletionRepository completion = new();
        ControlledLeaseRepository lease = new();
        RecordingScopeFactory scopes = new(index => index == 0
            ? ProcessingScope(completion)
            : RenewalScope(lease));
        ControlledDelay delay = new();
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay)
            .ProcessAsync(claim, LeaseDuration, HeartbeatInterval, default);
        (await delay.ReadAsync()).Release();
        await lease.WaitForInvocationAsync();
        ProcessClaimedSimulationJobResult.Completed expected =
            new(claim.Job.Id, Guid.NewGuid());

        completion.Complete(expected);
        lease.Complete(null);

        Assert.Equal(expected, await task);
    }

    [Fact]
    public async Task ProcessingFailure_RemainsPrimaryOverHeartbeatCleanupFailure()
    {
        InvalidOperationException primary = new("processing failed");
        IOException secondary = new("delay cleanup failed");
        ControlledCompletionRepository completion = new();
        completion.Fail(primary);
        ControlledDelay delay = new() { CancellationException = secondary };
        RecordingLogger<SimulationJobLeaseSupervisor> logger = new();
        RecordingScopeFactory scopes = new(_ => ProcessingScope(completion));

        Exception observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSupervisor(scopes, delay, logger).ProcessAsync(
                CreateClaim(), LeaseDuration, HeartbeatInterval, default));

        Assert.Same(primary, observed);
        Assert.Contains(nameof(ControlledCompletionRepository.SaveSimulationAndCompleteAsync),
            observed.StackTrace);
        Assert.Same(secondary, Assert.Single(logger.Entries).Exception);
    }

    [Fact]
    public async Task RenewalFailure_CancelsProcessingAndRemainsPrimary()
    {
        IOException primary = new("renew failed");
        ControlledCompletionRepository completion = new();
        ControlledLeaseRepository lease = new();
        lease.Fail(primary);
        RecordingScopeFactory scopes = new(index => index == 0
            ? ProcessingScope(completion)
            : RenewalScope(lease));
        ControlledDelay delay = new();
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay)
            .ProcessAsync(CreateClaim(), LeaseDuration, HeartbeatInterval, default);

        (await delay.ReadAsync()).Release();
        Exception observed = await Assert.ThrowsAsync<IOException>(() => task);

        Assert.Same(primary, observed);
        Assert.True(completion.ReceivedToken.IsCancellationRequested);
        Assert.Equal(2, scopes.CreateCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayFailure_CancelsProcessingAndPropagates(bool cancellation)
    {
        Exception primary = cancellation
            ? new OperationCanceledException("unexpected")
            : new IOException("delay failed");
        ControlledCompletionRepository completion = new();
        ControlledDelay delay = new();
        RecordingScopeFactory scopes = new(_ => ProcessingScope(completion));
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay)
            .ProcessAsync(CreateClaim(), LeaseDuration, HeartbeatInterval, default);

        (await delay.ReadAsync()).Fail(primary);
        Exception observed = await Assert.ThrowsAnyAsync<Exception>(() => task);

        Assert.Same(primary, observed);
        Assert.True(completion.ReceivedToken.IsCancellationRequested);
        Assert.Equal(1, scopes.CreateCount);
        Assert.Single(delay.Requests);
    }

    [Theory]
    [InlineData("delay")]
    [InlineData("renewal")]
    [InlineData("processing")]
    public async Task ExternalCancellation_PropagatesWithoutUnexpectedError(string phase)
    {
        ControlledCompletionRepository completion = new();
        ControlledLeaseRepository lease = new();
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobLeaseSupervisor> logger = new();
        RecordingScopeFactory scopes = new(index => index == 0
            ? ProcessingScope(completion)
            : RenewalScope(lease));
        using CancellationTokenSource cancellation = new();
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay, logger)
            .ProcessAsync(CreateClaim(), LeaseDuration, HeartbeatInterval, cancellation.Token);

        DelayRequest request = await delay.ReadAsync();
        if (phase == "renewal")
        {
            request.Release();
            await lease.WaitForInvocationAsync();
        }

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData("processing-create")]
    [InlineData("processing-resolve")]
    [InlineData("renewal-create")]
    [InlineData("renewal-resolve")]
    public async Task ScopeCreationOrResolutionFailure_Propagates(string phase)
    {
        IOException failure = new(phase);
        ControlledCompletionRepository completion = new();
        ControlledDelay delay = new();
        RecordingScopeFactory scopes = phase switch
        {
            "processing-create" => new(_ => throw failure),
            "processing-resolve" => new(_ => new RecordingScope(
                new ControlledServiceProvider(processingException: failure))),
            "renewal-create" => new(index => index == 0
                ? ProcessingScope(completion)
                : throw failure),
            _ => new(index => index == 0
                ? ProcessingScope(completion)
                : new RecordingScope(new ControlledServiceProvider(
                    renewalException: failure)))
        };
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay)
            .ProcessAsync(CreateClaim(), LeaseDuration, HeartbeatInterval, default);

        if (phase.StartsWith("renewal", StringComparison.Ordinal))
        {
            (await delay.ReadAsync()).Release();
        }

        Exception observed = await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task StandaloneRenewalDisposalFailure_Propagates()
    {
        IOException failure = new("renewal disposal failed");
        ControlledCompletionRepository completion = new();
        ControlledLeaseRepository lease = new(
            DateTimeOffset.Parse("2030-01-01T00:02:00Z"));
        RecordingScopeFactory scopes = new(index => index == 0
            ? ProcessingScope(completion)
            : RenewalScope(lease, failure));
        ControlledDelay delay = new();
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay)
            .ProcessAsync(CreateClaim(), LeaseDuration, HeartbeatInterval, default);

        (await delay.ReadAsync()).Release();
        Exception observed = await Assert.ThrowsAsync<IOException>(() => task);

        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task CompletedResult_WinsOverProcessingScopeDisposalFailure()
    {
        IOException secondary = new("processing disposal failed");
        SimulationJobClaim claim = CreateClaim();
        ProcessClaimedSimulationJobResult.Completed expected =
            new(claim.Job.Id, Guid.NewGuid());
        ControlledCompletionRepository completion = new(expected);
        RecordingLogger<SimulationJobLeaseSupervisor> logger = new();
        RecordingScopeFactory scopes = new(_ => ProcessingScope(completion, secondary));

        ProcessClaimedSimulationJobResult result = await CreateSupervisor(
            scopes, new ControlledDelay(), logger).ProcessAsync(
                claim, LeaseDuration, HeartbeatInterval, default);

        Assert.Equal(expected, result);
        Assert.Same(secondary, Assert.Single(logger.Entries).Exception);
    }

    [Fact]
    public async Task Logging_DoesNotExposeClaimTokenOrInformationSpam()
    {
        SimulationJobClaim claim = CreateClaim();
        ControlledCompletionRepository completion = new();
        ControlledLeaseRepository lease = new(null);
        RecordingLogger<SimulationJobLeaseSupervisor> logger = new();
        RecordingScopeFactory scopes = new(index => index == 0
            ? ProcessingScope(completion)
            : RenewalScope(lease));
        ControlledDelay delay = new();
        Task<ProcessClaimedSimulationJobResult> task = CreateSupervisor(scopes, delay, logger)
            .ProcessAsync(claim, LeaseDuration, HeartbeatInterval, default);

        (await delay.ReadAsync()).Release();
        _ = await task;

        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains(claim.ClaimToken.ToString(), StringComparison.Ordinal) ||
            entry.Properties.Values.Any(value => Equals(value, claim.ClaimToken)));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Contains(logger.Entries, entry =>
            entry.Properties.TryGetValue("JobId", out object? value) &&
            Equals(value, claim.Job.Id));
    }

    public static TheoryData<SimulationJobClaim?, TimeSpan, TimeSpan, string>
        InvalidArguments => new()
        {
            { null, LeaseDuration, HeartbeatInterval, "claim" },
            { CreateClaim(), TimeSpan.Zero, HeartbeatInterval, "leaseDuration" },
            { CreateClaim(), TimeSpan.FromTicks(-1), HeartbeatInterval, "leaseDuration" },
            { CreateClaim(), LeaseDuration, TimeSpan.Zero, "heartbeatInterval" },
            { CreateClaim(), LeaseDuration, TimeSpan.FromTicks(-1), "heartbeatInterval" },
            { CreateClaim(), LeaseDuration, LeaseDuration, "heartbeatInterval" },
            { CreateClaim(), LeaseDuration, LeaseDuration + TimeSpan.FromTicks(1), "heartbeatInterval" }
        };

    public static TheoryData<SimulationJobClaim, string>
        StructurallyInvalidClaims
    {
        get
        {
            SimulationJobClaim claim = CreateClaim();

            return new TheoryData<SimulationJobClaim, string>
            {
                {
                    claim with { Job = null! },
                    "Simulation job claim has no job snapshot."
                },
                {
                    claim with { Job = claim.Job with { Id = Guid.Empty } },
                    "Claimed simulation job has an empty ID."
                },
                {
                    claim with
                    {
                        Job = claim.Job with
                        {
                            Status = SimulationJobStatus.Pending
                        }
                    },
                    "Claimed simulation job is not Running."
                },
                {
                    claim with
                    {
                        Job = claim.Job with { ClaimedAtUtc = null }
                    },
                    "Claimed simulation job has no claim timestamp."
                },
                {
                    claim with { ClaimToken = Guid.Empty },
                    "Simulation job claim has an empty claim token."
                },
                {
                    claim with
                    {
                        LeaseExpiresAtUtc = claim.Job.ClaimedAtUtc!.Value
                    },
                    "Simulation job claim has an invalid lease interval."
                },
                {
                    claim with
                    {
                        LeaseExpiresAtUtc =
                            claim.Job.ClaimedAtUtc!.Value.AddTicks(-1)
                    },
                    "Simulation job claim has an invalid lease interval."
                },
                {
                    claim with
                    {
                        Job = claim.Job with
                        {
                            SimulationId = Guid.NewGuid()
                        }
                    },
                    "Claimed simulation job already has a simulation ID."
                },
                {
                    claim with
                    {
                        Job = claim.Job with
                        {
                            FinishedAtUtc = claim.Job.ClaimedAtUtc
                        }
                    },
                    "Claimed simulation job already has a finish timestamp."
                },
                {
                    claim with
                    {
                        Job = claim.Job with
                        {
                            FailureMessage = "unexpected"
                        }
                    },
                    "Claimed simulation job already has a failure message."
                },
                {
                    claim with
                    {
                        Job = claim.Job with { Request = null! }
                    },
                    "Claimed simulation job has no simulation request."
                }
            };
        }
    }

    private static SimulationJobLeaseSupervisor CreateSupervisor(
        RecordingScopeFactory scopes,
        ControlledDelay delay,
        RecordingLogger<SimulationJobLeaseSupervisor>? logger = null) =>
        new(scopes, delay, logger ?? new());

    private static SimulationJobClaim CreateClaim()
    {
        DateTimeOffset claimedAt = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        SimulationJobResult job = new(
            Guid.Parse("9dd8bde3-450c-4760-8cc2-5417c83a0502"),
            new RunSimulationRequest(2.5, 0.005, 0.1, 0.9, 4, SamplingMode.Uniform),
            SimulationJobStatus.Running,
            claimedAt.AddMinutes(-1),
            claimedAt,
            null,
            null,
            null);
        return new SimulationJobClaim(
            job,
            Guid.Parse("5cf6aefc-7ca9-43ba-a237-eb255b37c61d"),
            claimedAt.Add(LeaseDuration));
    }

    private static RecordingScope ProcessingScope(
        ControlledCompletionRepository completion,
        Exception? disposalException = null,
        ControlledJobRepository? jobs = null)
    {
        jobs ??= new ControlledJobRepository();
        ProcessClaimedSimulationJobHandler handler = new(
            new RunSimulationHandler(),
            completion,
            new FailSimulationJobHandler(jobs));
        completion.Handler = handler;
        return new RecordingScope(
            new ControlledServiceProvider(processingHandler: handler),
            disposalException);
    }

    private static RecordingScope RenewalScope(
        ControlledLeaseRepository lease,
        Exception? disposalException = null) =>
        new(new ControlledServiceProvider(
            renewalHandler: new RenewSimulationJobLeaseHandler(lease)),
            disposalException);

    private sealed class ControlledDelay : ISimulationJobWorkerDelay
    {
        private readonly Channel<DelayRequest> _requests =
            Channel.CreateUnbounded<DelayRequest>();
        public ConcurrentQueue<DelayRequest> Requests { get; } = new();
        public Exception? CancellationException { get; init; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayRequest request = new(delay, cancellationToken, CancellationException);
            Requests.Enqueue(request);
            _requests.Writer.TryWrite(request);
            return request.Task;
        }

        public async Task<DelayRequest> ReadAsync()
        {
            using CancellationTokenSource safety = new(SafetyTimeout);
            return await _requests.Reader.ReadAsync(safety.Token);
        }
    }

    private sealed class DelayRequest
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public DelayRequest(TimeSpan duration, CancellationToken token,
            Exception? cancellationException)
        {
            Duration = duration;
            Token = token;
            _registration = token.Register(() =>
            {
                if (cancellationException is null)
                {
                    _completion.TrySetCanceled(token);
                }
                else
                {
                    _completion.TrySetException(cancellationException);
                }
            });
        }

        public TimeSpan Duration { get; }
        public CancellationToken Token { get; }
        public Task Task => _completion.Task;
        public void Release() { _registration.Dispose(); _completion.TrySetResult(); }
        public void Fail(Exception exception)
        { _registration.Dispose(); _completion.TrySetException(exception); }
    }

    private sealed class RecordingScopeFactory : IServiceScopeFactory
    {
        private readonly Func<int, RecordingScope> _factory;
        public RecordingScopeFactory(Func<int, RecordingScope> factory) => _factory = factory;
        public int CreateCount { get; private set; }
        public List<RecordingScope> Scopes { get; } = new();
        public IServiceScope CreateScope()
        {
            RecordingScope scope = _factory(CreateCount++);
            Scopes.Add(scope);
            return scope;
        }
    }

    private sealed class RecordingScope : IServiceScope, IAsyncDisposable
    {
        private readonly Exception? _disposalException;
        public RecordingScope(ControlledServiceProvider provider,
            Exception? disposalException = null)
        { Provider = provider; _disposalException = disposalException; }
        public ControlledServiceProvider Provider { get; }
        public IServiceProvider ServiceProvider => Provider;
        public int AsyncDisposeCount { get; private set; }
        public void Dispose() => throw new InvalidOperationException("Synchronous disposal used.");
        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return _disposalException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(_disposalException);
        }
    }

    private sealed class ControlledServiceProvider : IServiceProvider
    {
        private readonly ProcessClaimedSimulationJobHandler? _processingHandler;
        private readonly RenewSimulationJobLeaseHandler? _renewalHandler;
        private readonly Exception? _processingException;
        private readonly Exception? _renewalException;
        public ControlledServiceProvider(
            ProcessClaimedSimulationJobHandler? processingHandler = null,
            RenewSimulationJobLeaseHandler? renewalHandler = null,
            Exception? processingException = null,
            Exception? renewalException = null)
        {
            _processingHandler = processingHandler; _renewalHandler = renewalHandler;
            _processingException = processingException; _renewalException = renewalException;
        }
        public int ProcessingResolutions { get; private set; }
        public int RenewalResolutions { get; private set; }
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ProcessClaimedSimulationJobHandler))
            {
                ProcessingResolutions++; if (_processingException is not null) throw _processingException;
                return _processingHandler;
            }
            if (serviceType == typeof(RenewSimulationJobLeaseHandler))
            {
                RenewalResolutions++; if (_renewalException is not null) throw _renewalException;
                return _renewalHandler;
            }
            return null;
        }
    }

    private sealed class ControlledCompletionRepository : ISimulationJobCompletionRepository
    {
        private readonly TaskCompletionSource<ProcessClaimedSimulationJobResult>? _gate;
        private readonly ProcessClaimedSimulationJobResult? _immediate;
        public ControlledCompletionRepository(ProcessClaimedSimulationJobResult? immediate = null)
        { _immediate = immediate; if (immediate is null) _gate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public ProcessClaimedSimulationJobHandler? Handler { get; set; }
        public SimulationJobClaim? ReceivedClaim { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public int FencedCount { get; private set; }
        public void Complete(ProcessClaimedSimulationJobResult result) => _gate!.TrySetResult(result);
        public void Fail(Exception exception) => (_gate ?? throw new InvalidOperationException()).TrySetException(exception);
        public async Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
            Guid jobId, Guid claimToken, RunSimulationResult simulation,
            CancellationToken cancellationToken)
        {
            FencedCount++;
            ReceivedToken = cancellationToken;
            ReceivedClaim = CreateClaim();
            ProcessClaimedSimulationJobResult result = _immediate ??
                await _gate!.Task.WaitAsync(cancellationToken);
            return result switch
            {
                ProcessClaimedSimulationJobResult.Completed completed =>
                    ReceivedClaim.Job with
                    {
                        Status = SimulationJobStatus.Completed,
                        SimulationId = completed.SimulationId,
                        FinishedAtUtc = DateTimeOffset.Parse("2030-01-01T00:01:00Z")
                    },
                ProcessClaimedSimulationJobResult.LeaseLost => null,
                _ => throw new UnreachableException()
            };
        }
    }

    private sealed class ControlledLeaseRepository : ISimulationJobLeaseRepository
    {
        private readonly TaskCompletionSource _invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DateTimeOffset?>? _gate;
        private readonly DateTimeOffset? _immediate;
        private readonly bool _hasImmediate;
        public ControlledLeaseRepository() { _gate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public ControlledLeaseRepository(DateTimeOffset? result) { _immediate = result; _hasImmediate = true; }
        public Guid JobId { get; private set; }
        public Guid ClaimToken { get; private set; }
        public TimeSpan LeaseDuration { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Count { get; private set; }
        public void Complete(DateTimeOffset? result) => _gate!.TrySetResult(result);
        public void Fail(Exception exception) => _gate!.TrySetException(exception);
        public async Task WaitForInvocationAsync()
        { using CancellationTokenSource safety = new(SafetyTimeout); await _invoked.Task.WaitAsync(safety.Token); }
        public Task<SimulationJobClaim?> ClaimNextPendingAsync(TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<DateTimeOffset?> TryRenewLeaseAsync(Guid jobId, Guid claimToken,
            TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            Count++; JobId = jobId; ClaimToken = claimToken; LeaseDuration = leaseDuration;
            Token = cancellationToken; _invoked.TrySetResult();
            return _hasImmediate ? _immediate : await _gate!.Task.WaitAsync(cancellationToken);
        }
        public Task<IReadOnlyList<Guid>> RecoverExpiredAsync(int batchSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ControlledJobRepository : ISimulationJobRepository
    {
        public int FencedFailureCount { get; private set; }

        public Task<SimulationJobResult> CreateAsync(RunSimulationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> GetByIdAsync(Guid id,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> TryCompleteAsync(Guid jobId, Guid claimToken,
            Guid simulationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> TryFailAsync(Guid jobId, Guid claimToken,
            string failureMessage, CancellationToken cancellationToken)
        {
            FencedFailureCount++;
            return Task.FromResult<SimulationJobResult?>(null);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            IReadOnlyDictionary<string, object?> properties = state is
                IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Enqueue(new(logLevel, exception, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception,
        string Message, IReadOnlyDictionary<string, object?> Properties);
}