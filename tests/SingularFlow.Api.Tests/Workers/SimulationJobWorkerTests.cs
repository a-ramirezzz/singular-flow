using System.Collections.Concurrent;
using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SingularFlow.Api.Workers;
using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Workers;

public sealed class SimulationJobWorkerTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(11);

    [Fact]
    public async Task DisabledWorker_PerformsNoRuntimeActivity()
    {
        WorkerHarness harness = new(enabled: false);

        await harness.Worker.StartAsync(CancellationToken.None);
        await harness.Worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, harness.Leases.ClaimCount);
        Assert.Equal(0, harness.Leases.RecoveryCount);
        Assert.Empty(harness.Delay.Requests);
        Assert.DoesNotContain(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Information);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task EnabledWorker_ClaimsAndRecoversImmediatelyThenPolls()
    {
        WorkerHarness harness = new();

        await harness.Worker.StartAsync(CancellationToken.None);
        DelayRequest first = await harness.Delay.ReadAsync();
        DelayRequest second = await harness.Delay.ReadAsync();

        Assert.Equal(1, harness.Leases.ClaimCount);
        Assert.Equal(LeaseDuration, harness.Leases.ReceivedLeaseDuration);
        Assert.Equal(1, harness.Leases.RecoveryCount);
        Assert.Equal(100, harness.Leases.ReceivedBatchSize);
        Assert.Contains(
            new[] { first.Duration, second.Duration },
            duration => duration == PollingInterval);
        Assert.Contains(
            new[] { first.Duration, second.Duration },
            duration => duration == RecoveryInterval);
        Assert.All(harness.Leases.ReceivedTokens, token => Assert.False(token.IsCancellationRequested));

        await harness.Worker.StopAsync(CancellationToken.None);
        Assert.All(harness.Delay.Requests, request => Assert.True(request.Token.IsCancellationRequested));
        await harness.DisposeAsync();
    }

    [Theory]
    [InlineData(typeof(ClaimSimulationJobLeaseHandler))]
    [InlineData(typeof(ProcessClaimedSimulationJobHandler))]
    [InlineData(typeof(RenewSimulationJobLeaseHandler))]
    [InlineData(typeof(RecoverExpiredSimulationJobsHandler))]
    public async Task StartupValidation_MissingRequiredHandler_FailsBeforeLoops(
        Type missingService)
    {
        WorkerHarness harness = new(missingService: missingService);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Worker.StartAsync(CancellationToken.None));

        Assert.Equal(0, harness.Leases.ClaimCount);
        Assert.Equal(0, harness.Leases.RecoveryCount);
        Assert.Empty(harness.Delay.Requests);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task CompletedClaim_LogsIdentifiersWithoutClaimToken()
    {
        Guid jobId = Guid.NewGuid();
        Guid simulationId = Guid.NewGuid();
        Guid claimToken = Guid.NewGuid();
        WorkerHarness harness = new();
        harness.Leases.Claims.Enqueue(CreateClaim(jobId, claimToken));
        harness.Completion.Result = CreateCompletedJob(jobId, simulationId);

        await harness.Worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Leases.ClaimCount >= 2);

        LogEntry completed = Assert.Single(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                entry.Properties.ContainsKey("SimulationId"));
        Assert.Equal(jobId, completed.Properties["JobId"]);
        Assert.Equal(simulationId, completed.Properties["SimulationId"]);
        Assert.DoesNotContain(
            claimToken.ToString(),
            string.Join('|', harness.Logger.Entries.Select(Render)),
            StringComparison.OrdinalIgnoreCase);

        await harness.Worker.StopAsync(CancellationToken.None);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task LeaseLost_LogsWarningAndClaimsAgainImmediately()
    {
        Guid jobId = Guid.NewGuid();
        Guid claimToken = Guid.NewGuid();
        WorkerHarness harness = new();
        harness.Leases.Claims.Enqueue(CreateClaim(jobId, claimToken));

        await harness.Worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Leases.ClaimCount >= 2);

        LogEntry warning = Assert.Single(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Properties.ContainsKey("JobId"));
        Assert.Equal(jobId, warning.Properties["JobId"]);
        Assert.DoesNotContain(
            claimToken.ToString(),
            Render(warning),
            StringComparison.OrdinalIgnoreCase);

        await harness.Worker.StopAsync(CancellationToken.None);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task ClaimFailure_LogsErrorAndUsesFailureDelay()
    {
        InvalidOperationException failure = new("claim failed");
        WorkerHarness harness = new();
        harness.Leases.ClaimException = failure;

        await harness.Worker.StartAsync(CancellationToken.None);
        DelayRequest request = await ReadDurationAsync(harness.Delay, FailureDelay);

        Assert.Equal(FailureDelay, request.Duration);
        Assert.Same(
            failure,
            Assert.Single(harness.Logger.Entries, entry => entry.Exception == failure)
                .Exception);
        await WaitUntilAsync(() => harness.Leases.RecoveryCount == 1);
        Assert.Equal(1, harness.Leases.RecoveryCount);

        await harness.Worker.StopAsync(CancellationToken.None);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task RecoveryFailure_UsesFailureDelayWhileProcessingContinues()
    {
        InvalidOperationException failure = new("recovery failed");
        WorkerHarness harness = new();
        harness.Leases.RecoveryException = failure;

        await harness.Worker.StartAsync(CancellationToken.None);
        _ = await ReadDurationAsync(harness.Delay, FailureDelay);

        Assert.Equal(1, harness.Leases.ClaimCount);
        Assert.Same(
            failure,
            Assert.Single(harness.Logger.Entries, entry => entry.Exception == failure)
                .Exception);

        await harness.Worker.StopAsync(CancellationToken.None);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task PollingDelayFailure_FaultsWorkerAndCancelsRecovery()
    {
        IOException failure = new("poll delay failed");
        WorkerHarness harness = new();

        await harness.Worker.StartAsync(CancellationToken.None);
        DelayRequest polling = await ReadDurationAsync(harness.Delay, PollingInterval);
        polling.Fail(failure);

        Exception observed = await Assert.ThrowsAsync<IOException>(
            () => harness.Worker.ExecuteTask!.WaitAsync(SafetyTimeout));
        Assert.Same(failure, observed);
        Assert.Single(
            harness.Delay.Requests,
            request => request.Duration == PollingInterval);
        Assert.Contains(
            harness.Delay.Requests,
            request => request.Duration == RecoveryInterval &&
                request.Token.IsCancellationRequested);
        await harness.DisposeAsync();
    }

    [Fact]
    public void ValidateResult_Null_ThrowsInvariantViolation()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SimulationJobWorker.ValidateResult(null));

        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SimulationJobClaim CreateClaim(Guid jobId, Guid claimToken)
    {
        DateTimeOffset claimedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        SimulationJobResult job = new(
            jobId,
            new RunSimulationRequest(2.5, 0.005, 0.1, 0.9, 4, SamplingMode.Uniform),
            SimulationJobStatus.Running,
            claimedAt.AddMinutes(-1),
            claimedAt,
            null,
            null,
            null);
        return new SimulationJobClaim(job, claimToken, claimedAt + LeaseDuration);
    }

    private static SimulationJobResult CreateCompletedJob(Guid jobId, Guid simulationId)
    {
        SimulationJobResult running = CreateClaim(jobId, Guid.NewGuid()).Job;
        return running with
        {
            Status = SimulationJobStatus.Completed,
            SimulationId = simulationId,
            FinishedAtUtc = running.ClaimedAtUtc!.Value.AddSeconds(1)
        };
    }

    private static async Task<DelayRequest> ReadDurationAsync(
        ControlledDelay delay,
        TimeSpan duration)
    {
        while (true)
        {
            DelayRequest request = await delay.ReadAsync();
            if (request.Duration == duration)
            {
                return request;
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using CancellationTokenSource cancellation = new(SafetyTimeout);
        while (!predicate())
        {
            await Task.Yield();
            cancellation.Token.ThrowIfCancellationRequested();
        }
    }

    private static string Render(LogEntry entry) =>
        $"{entry.Message}|{string.Join('|', entry.Properties.Values)}";

    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        public WorkerHarness(bool enabled = true, Type? missingService = null)
        {
            Leases = new ControlledLeaseRepository();
            Completion = new ControlledCompletionRepository();
            Delay = new ControlledDelay();
            Logger = new RecordingLogger<SimulationJobWorker>();
            ServiceCollection services = new();
            services.AddSingleton(Leases);
            services.AddSingleton(Completion);
            services.AddScoped<ISimulationJobLeaseRepository>(provider => provider.GetRequiredService<ControlledLeaseRepository>());
            services.AddScoped<ISimulationJobRepository, ControlledJobRepository>();
            services.AddScoped<ISimulationJobCompletionRepository>(provider => provider.GetRequiredService<ControlledCompletionRepository>());
            services.AddScoped<RunSimulationHandler>();
            services.AddScoped<FailSimulationJobHandler>();
            services.AddScoped<ClaimSimulationJobLeaseHandler>();
            services.AddScoped<RenewSimulationJobLeaseHandler>();
            services.AddScoped<RecoverExpiredSimulationJobsHandler>();
            services.AddScoped<ProcessClaimedSimulationJobHandler>();
            if (missingService is not null)
            {
                services.RemoveAll(missingService);
            }
            _services = services.BuildServiceProvider(validateScopes: true);
            IServiceScopeFactory scopes = _services.GetRequiredService<IServiceScopeFactory>();
            SimulationJobLeaseSupervisor supervisor = new(
                scopes,
                Delay,
                new RecordingLogger<SimulationJobLeaseSupervisor>());
            Worker = new SimulationJobWorker(
                scopes,
                Options.Create(new SimulationJobWorkerOptions
                {
                    Enabled = enabled,
                    PollingInterval = PollingInterval,
                    FailureDelay = FailureDelay,
                    LeaseDuration = LeaseDuration,
                    HeartbeatInterval = HeartbeatInterval,
                    RecoveryInterval = RecoveryInterval,
                    RecoveryBatchSize = 100
                }),
                Delay,
                supervisor,
                Logger);
        }

        public ControlledLeaseRepository Leases { get; }
        public ControlledCompletionRepository Completion { get; }
        public ControlledDelay Delay { get; }
        public RecordingLogger<SimulationJobWorker> Logger { get; }
        public SimulationJobWorker Worker { get; }

        public async ValueTask DisposeAsync()
        {
            Worker.Dispose();
            await _services.DisposeAsync();
        }
    }

    private sealed class ControlledLeaseRepository : ISimulationJobLeaseRepository
    {
        public ConcurrentQueue<SimulationJobClaim?> Claims { get; } = new();
        public int ClaimCount { get; private set; }
        public int RecoveryCount { get; private set; }
        public TimeSpan ReceivedLeaseDuration { get; private set; }
        public int ReceivedBatchSize { get; private set; }
        public List<CancellationToken> ReceivedTokens { get; } = [];
        public Exception? ClaimException { get; set; }
        public Exception? RecoveryException { get; set; }

        public Task<SimulationJobClaim?> ClaimNextPendingAsync(TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            ClaimCount++;
            ReceivedLeaseDuration = leaseDuration;
            ReceivedTokens.Add(cancellationToken);
            if (ClaimException is not null) throw ClaimException;
            return Task.FromResult(Claims.TryDequeue(out SimulationJobClaim? claim) ? claim : null);
        }

        public Task<DateTimeOffset?> TryRenewLeaseAsync(Guid jobId, Guid claimToken, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(DateTimeOffset.UtcNow + leaseDuration);

        public Task<IReadOnlyList<Guid>> RecoverExpiredAsync(int batchSize, CancellationToken cancellationToken)
        {
            RecoveryCount++;
            ReceivedBatchSize = batchSize;
            ReceivedTokens.Add(cancellationToken);
            if (RecoveryException is not null) throw RecoveryException;
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }
    }

    private sealed class ControlledJobRepository : ISimulationJobRepository
    {
        public Task<SimulationJobResult> CreateAsync(RunSimulationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> TryCompleteAsync(Guid jobId, Guid claimToken, Guid simulationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SimulationJobResult?> TryFailAsync(Guid jobId, Guid claimToken, string failureMessage, CancellationToken cancellationToken) => Task.FromResult<SimulationJobResult?>(null);
    }

    private sealed class ControlledCompletionRepository : ISimulationJobCompletionRepository
    {
        public SimulationJobResult? Result { get; set; }
        public Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(Guid jobId, Guid claimToken, RunSimulationResult simulation, CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    private sealed class ControlledDelay : ISimulationJobWorkerDelay
    {
        private readonly Channel<DelayRequest> _channel = Channel.CreateUnbounded<DelayRequest>();
        public ConcurrentQueue<DelayRequest> Requests { get; } = new();
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayRequest request = new(delay, cancellationToken);
            Requests.Enqueue(request);
            _channel.Writer.TryWrite(request);
            return request.Task;
        }
        public async Task<DelayRequest> ReadAsync()
        {
            using CancellationTokenSource cancellation = new(SafetyTimeout);
            return await _channel.Reader.ReadAsync(cancellation.Token);
        }
    }

    private sealed class DelayRequest
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DelayRequest(TimeSpan duration, CancellationToken token)
        {
            Duration = duration;
            Token = token;
            token.Register(() => _completion.TrySetCanceled(token));
        }
        public TimeSpan Duration { get; }
        public CancellationToken Token { get; }
        public Task Task => _completion.Task;
        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            IReadOnlyDictionary<string, object?> properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Enqueue(new LogEntry(logLevel, exception, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message, IReadOnlyDictionary<string, object?> Properties);
}