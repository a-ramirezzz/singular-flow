using System.Collections.Concurrent;
using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SingularFlow.Api.Workers;
using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Workers;

public sealed class SimulationJobWorkerTests
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task DisabledWorker_CreatesNoScopeOrDelay_AndLogsOnce()
    {
        ControlledDelay delay = new();
        RecordingScopeFactory scopes = new(_ => CreateScope(CreateNoWorkHandler()));
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(false, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        _ = await logger.ReadEntryAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, scopes.CreateCount);
        Assert.Empty(delay.Requests);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Debug);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task StartAsync_Enabled_ResolvesOnceAndDisposesValidationScopeAsync()
    {
        RecordingScope validation = CreateScope(CreateNoWorkHandler());
        ControlledJobRepository processingRepository = new()
        {
            BlockClaimUntilCancellation = true
        };
        RecordingScopeFactory scopes = new(index => index == 0
            ? validation
            : CreateScope(CreateHandler(processingRepository)));
        SimulationJobWorker worker = CreateWorker(true, scopes, new ControlledDelay());

        await worker.StartAsync(CancellationToken.None);

        Assert.Equal(1, validation.Provider.ResolutionCount);
        Assert.Equal(1, validation.AsyncDisposeCount);
        Assert.Equal(0, validation.SyncDisposeCount);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_PreCancelledToken_CreatesNoValidationScope()
    {
        RecordingScopeFactory scopes = new(_ => CreateScope(CreateNoWorkHandler()));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        SimulationJobWorker worker = CreateWorker(true, scopes, new ControlledDelay());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.StartAsync(cancellation.Token));

        Assert.Equal(0, scopes.CreateCount);
    }

    [Fact]
    public async Task StartAsync_MissingHandler_FailsAndDisposesValidationScope()
    {
        RecordingScope validation = new(new ControlledServiceProvider());
        RecordingScopeFactory scopes = new(_ => validation);
        SimulationJobWorker worker = CreateWorker(true, scopes, new ControlledDelay());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));

        Assert.Equal(1, validation.Provider.ResolutionCount);
        Assert.Equal(1, validation.AsyncDisposeCount);
        Assert.Equal(1, scopes.CreateCount);
    }

    [Fact]
    public async Task StartAsync_ResolutionAndDisposalFail_ResolutionRemainsPrimary()
    {
        InvalidOperationException primary = new("resolution failed");
        IOException secondary = new("disposal failed");
        RecordingScope validation = new(
            new ControlledServiceProvider(exception: primary),
            secondary);
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(
            true,
            new RecordingScopeFactory(_ => validation),
            new ControlledDelay(),
            logger);

        Exception observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));

        Assert.Same(primary, observed);
        Assert.Contains(nameof(ControlledServiceProvider.GetService), observed.StackTrace);
        Assert.Same(
            secondary,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning)
                .Exception);
    }

    [Fact]
    public async Task StartAsync_WhenSecondaryWarningThrows_PreservesResolutionFailure()
    {
        InvalidOperationException primary = new("resolution failed");
        IOException secondary = new("disposal failed");
        ApplicationException loggerFailure = new("warning logger failed");
        RecordingScope validation = new(
            new ControlledServiceProvider(exception: primary),
            secondary);
        RecordingScopeFactory scopes = new(_ => validation);
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new()
        {
            ThrowOnLevel = LogLevel.Warning,
            LoggingException = loggerFailure
        };
        using SimulationJobWorker worker = CreateWorker(
            true,
            scopes,
            delay,
            logger);

        Exception observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));

        Assert.Same(primary, observed);
        Assert.NotSame(secondary, observed);
        Assert.NotSame(loggerFailure, observed);
        Assert.Contains(nameof(ControlledServiceProvider.GetService), observed.StackTrace);
        Assert.Equal(1, scopes.CreateCount);
        Assert.Empty(delay.Requests);
        Assert.Equal(1, logger.ThrowCount);
    }

    [Fact]
    public async Task StartAsync_WhenCancellationAndDisposalFail_PreservesCancellation()
    {
        using CancellationTokenSource startupCancellation = new();
        IOException secondary = new("disposal failed");
        RecordingScope validation = CreateScope(CreateNoWorkHandler(), secondary);
        RecordingScopeFactory scopes = new(
            _ => validation,
            startupCancellation.Cancel);
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new();
        using SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        OperationCanceledException observed =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => worker.StartAsync(startupCancellation.Token));

        Assert.Equal(startupCancellation.Token, observed.CancellationToken);
        Assert.Equal(1, scopes.CreateCount);
        Assert.Equal(1, validation.AsyncDisposeCount);
        Assert.Empty(delay.Requests);
        Assert.Same(
            secondary,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning)
                .Exception);
    }

    [Fact]
    public async Task StartAsync_DisposalFailsAfterResolution_PropagatesDisposalFailure()
    {
        IOException failure = new("disposal failed");
        RecordingScope validation = CreateScope(CreateNoWorkHandler(), failure);
        SimulationJobWorker worker = CreateWorker(
            true,
            new RecordingScopeFactory(_ => validation),
            new ControlledDelay());

        Exception observed = await Assert.ThrowsAsync<IOException>(
            () => worker.StartAsync(CancellationToken.None));

        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task NoWork_DisposesScopeThenRequestsPollingDelayWithExactToken()
    {
        ControlledDelay delay = new();
        RecordingScope? processing = null;
        ControlledJobRepository repository = new();
        delay.DisposalProbe = () => processing?.AsyncDisposeCount == 1;
        RecordingScopeFactory scopes = new(index =>
        {
            RecordingScope scope = index == 0
                ? CreateScope(CreateNoWorkHandler())
                : CreateScope(CreateHandler(repository));
            if (index == 1)
            {
                processing = scope;
            }

            return scope;
        });
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();

        Assert.Equal(PollingInterval, request.Duration);
        Assert.Equal(1, processing!.AsyncDisposeCount);
        Assert.True(request.ScopeWasDisposed);
        Assert.Equal(repository.ReceivedToken, request.CancellationToken);
        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                entry.Message.Contains("started", StringComparison.OrdinalIgnoreCase));

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Completed_LogsIdsAndStartsFreshScopeWithoutPollingDelay()
    {
        Guid jobId = Guid.NewGuid();
        Guid simulationId = Guid.NewGuid();
        ControlledJobRepository first = new() { ClaimResult = CreateRunningJob(jobId) };
        ControlledCompletionRepository completion = new()
        {
            Result = CreateCompletedJob(jobId, simulationId)
        };
        ControlledJobRepository second = new();
        RecordingScopeFactory scopes = new(index => index switch
        {
            0 => CreateScope(CreateNoWorkHandler()),
            1 => CreateScope(CreateHandler(first, completion)),
            _ => CreateScope(CreateHandler(second))
        });
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        _ = await logger.ReadEntryAsync();
        LogEntry completed = await logger.ReadEntryAsync();
        DelayRequest nextIterationDelay = await delay.ReadRequestAsync();

        Assert.Null(completed.Exception);
        Assert.Equal(LogLevel.Information, completed.Level);
        Assert.Contains("completed", completed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PollingInterval, nextIterationDelay.Duration);
        Assert.Equal(3, scopes.CreateCount);
        Assert.Single(delay.Requests);
        Assert.Equal(jobId, completed.Properties["JobId"]);
        Assert.Equal(simulationId, completed.Properties["SimulationId"]);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OrdinaryHandlerFailure_LogsOnceAndDelaysAfterScopeDisposal()
    {
        InvalidOperationException failure = new("claim failed");
        ControlledJobRepository repository = new() { ClaimException = failure };
        RecordingScope? processing = null;
        ControlledDelay delay = new();
        delay.DisposalProbe = () => processing?.AsyncDisposeCount == 1;
        RecordingScopeFactory scopes = new(index =>
        {
            RecordingScope scope = index == 0
                ? CreateScope(CreateNoWorkHandler())
                : CreateScope(CreateHandler(repository));
            if (index == 1)
            {
                processing = scope;
            }

            return scope;
        });
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();

        Assert.Equal(FailureDelay, request.Duration);
        Assert.True(request.ScopeWasDisposed);
        Assert.Same(
            failure,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error)
                .Exception);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UnexpectedHandlerCancellation_IsRecoverableFailure()
    {
        OperationCanceledException failure = new("unexpected");
        ControlledJobRepository repository = new() { ClaimException = failure };
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(
            true,
            CreateStandardScopes(repository),
            delay,
            logger);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();

        Assert.Equal(FailureDelay, request.Duration);
        Assert.Same(
            failure,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error)
                .Exception);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostCancellationDuringHandler_ExitsWithoutErrorOrDelay()
    {
        ControlledJobRepository repository = new() { BlockClaimUntilCancellation = true };
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(
            true,
            CreateStandardScopes(repository),
            delay,
            logger);

        await worker.StartAsync(CancellationToken.None);
        await repository.WaitForClaimAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(delay.Requests);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCancellationDuringDelay_ExitsNormally(bool processingFails)
    {
        ControlledJobRepository repository = new();
        if (processingFails)
        {
            repository.ClaimException = new InvalidOperationException("failure");
        }

        ControlledDelay delay = new();
        SimulationJobWorker worker = CreateWorker(
            true,
            CreateStandardScopes(repository),
            delay);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(processingFails ? FailureDelay : PollingInterval, request.Duration);
        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.Single(delay.Requests);
    }

    [Fact]
    public async Task EveryIteration_CreatesAndDisposesNewScope()
    {
        ControlledDelay delay = new();
        RecordingScopeFactory scopes = CreateStandardScopes(new ControlledJobRepository());
        SimulationJobWorker worker = CreateWorker(true, scopes, delay);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest first = await delay.ReadRequestAsync();
        first.Release();
        _ = await delay.ReadRequestAsync();

        Assert.Equal(3, scopes.CreateCount);
        Assert.NotSame(scopes.CreatedScopes[1], scopes.CreatedScopes[2]);
        Assert.Equal(1, scopes.CreatedScopes[1].AsyncDisposeCount);
        Assert.Equal(1, scopes.CreatedScopes[2].AsyncDisposeCount);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisposalFailureAfterNoWork_BecomesDelayedIterationFailure()
    {
        IOException failure = new("dispose failed");
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new();
        RecordingScopeFactory scopes = new(index => index == 1
            ? CreateScope(CreateNoWorkHandler(), failure)
            : CreateScope(CreateNoWorkHandler()));
        SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();

        Assert.Equal(FailureDelay, request.Duration);
        Assert.Same(
            failure,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error)
                .Exception);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandlerAndDisposalFail_HandlerRemainsPrimary()
    {
        InvalidOperationException primary = new("handler failed");
        IOException secondary = new("dispose failed");
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new();
        ControlledJobRepository repository = new() { ClaimException = primary };
        RecordingScopeFactory scopes = new(index => index == 1
            ? CreateScope(CreateHandler(repository), secondary)
            : CreateScope(CreateNoWorkHandler()));
        SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();

        Assert.Equal(FailureDelay, request.Duration);
        Assert.Same(
            primary,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error)
                .Exception);
        Assert.Same(
            secondary,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning)
                .Exception);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Processing_WhenSecondaryWarningThrows_PreservesHandlerFailure()
    {
        InvalidOperationException primary = new("handler failed");
        IOException secondary = new("disposal failed");
        ApplicationException loggerFailure = new("warning logger failed");
        ControlledJobRepository repository = new() { ClaimException = primary };
        RecordingScope? processing = null;
        RecordingScopeFactory scopes = new(index =>
        {
            RecordingScope scope = index == 1
                ? CreateScope(CreateHandler(repository), secondary)
                : CreateScope(CreateNoWorkHandler());
            if (index == 1)
            {
                processing = scope;
            }

            return scope;
        });
        ControlledDelay delay = new()
        {
            DisposalProbe = () => processing?.AsyncDisposeCount == 1
        };
        RecordingLogger<SimulationJobWorker> logger = new()
        {
            ThrowOnLevel = LogLevel.Warning,
            LoggingException = loggerFailure
        };
        using SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();

        Assert.Equal(FailureDelay, request.Duration);
        Assert.True(request.ScopeWasDisposed);
        Assert.Single(delay.Requests);
        LogEntry error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Same(primary, error.Exception);
        Assert.NotSame(secondary, error.Exception);
        Assert.NotSame(loggerFailure, error.Exception);
        Assert.Equal(1, logger.ThrowCount);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostCancellationAndDisposalFail_CancellationRemainsPrimary()
    {
        IOException secondary = new("dispose failed");
        ControlledJobRepository repository = new() { BlockClaimUntilCancellation = true };
        RecordingLogger<SimulationJobWorker> logger = new();
        ControlledDelay delay = new();
        RecordingScopeFactory scopes = new(index => index == 1
            ? CreateScope(CreateHandler(repository), secondary)
            : CreateScope(CreateNoWorkHandler()));
        SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        await repository.WaitForClaimAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(delay.Requests);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(
            secondary,
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning)
                .Exception);
    }

    [Fact]
    public async Task HostCancellation_WhenSecondaryWarningThrows_ExitsCleanly()
    {
        IOException secondary = new("disposal failed");
        ApplicationException loggerFailure = new("warning logger failed");
        ControlledJobRepository repository = new()
        {
            BlockClaimUntilCancellation = true
        };
        RecordingScopeFactory scopes = new(index => index == 1
            ? CreateScope(CreateHandler(repository), secondary)
            : CreateScope(CreateNoWorkHandler()));
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new()
        {
            ThrowOnLevel = LogLevel.Warning,
            LoggingException = loggerFailure
        };
        using SimulationJobWorker worker = CreateWorker(true, scopes, delay, logger);

        await worker.StartAsync(CancellationToken.None);
        await repository.WaitForClaimAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(delay.Requests);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.DoesNotContain(logger.Entries, entry => entry.Exception == secondary);
        Assert.DoesNotContain(logger.Entries, entry => entry.Exception == loggerFailure);
        Assert.Equal(1, logger.ThrowCount);
    }

    [Fact]
    public void ValidateResult_Null_ThrowsIterationFailure()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SimulationJobWorker.ValidateResult(null));

        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayFailure_FaultsWorkerWithoutRetry(bool cancellationFailure)
    {
        Exception failure = cancellationFailure
            ? new OperationCanceledException("unexpected delay cancellation")
            : new IOException("delay failed");
        ControlledDelay delay = new();
        SimulationJobWorker worker = CreateWorker(
            true,
            CreateStandardScopes(new ControlledJobRepository()),
            delay);

        await worker.StartAsync(CancellationToken.None);
        DelayRequest request = await delay.ReadRequestAsync();
        request.Fail(failure);

        Exception observed = await Assert.ThrowsAnyAsync<Exception>(
            () => WaitWithSafetyCancellationAsync(worker.ExecuteTask!));
        Assert.Same(failure, observed);
        Assert.Single(delay.Requests);
    }

    [Fact]
    public async Task EnabledWorker_LogsStartAndStopExactlyOnce()
    {
        ControlledDelay delay = new();
        RecordingLogger<SimulationJobWorker> logger = new();
        SimulationJobWorker worker = CreateWorker(
            true,
            CreateStandardScopes(new ControlledJobRepository()),
            delay,
            logger);

        await worker.StartAsync(CancellationToken.None);
        _ = await delay.ReadRequestAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                entry.Message.Contains("started", StringComparison.OrdinalIgnoreCase));
        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                entry.Message.Contains("stopping", StringComparison.OrdinalIgnoreCase));
    }

    private static SimulationJobWorker CreateWorker(
        bool enabled,
        RecordingScopeFactory scopes,
        ControlledDelay delay,
        RecordingLogger<SimulationJobWorker>? logger = null)
    {
        return new SimulationJobWorker(
            scopes,
            Options.Create(new SimulationJobWorkerOptions
            {
                Enabled = enabled,
                PollingInterval = PollingInterval,
                FailureDelay = FailureDelay
            }),
            delay,
            logger ?? new RecordingLogger<SimulationJobWorker>());
    }

    private static async Task WaitWithSafetyCancellationAsync(Task task)
    {
        using CancellationTokenSource safetyCancellation = new(SafetyTimeout);
        await task.WaitAsync(safetyCancellation.Token);
    }

    private static RecordingScopeFactory CreateStandardScopes(
        ControlledJobRepository repository)
    {
        return new RecordingScopeFactory(index => index == 0
            ? CreateScope(CreateNoWorkHandler())
            : CreateScope(CreateHandler(repository)));
    }

    private static RecordingScope CreateScope(
        ProcessNextSimulationJobHandler handler,
        Exception? disposeException = null)
    {
        return new RecordingScope(new ControlledServiceProvider(handler), disposeException);
    }

    private static ProcessNextSimulationJobHandler CreateNoWorkHandler()
    {
        return CreateHandler(new ControlledJobRepository());
    }

    private static ProcessNextSimulationJobHandler CreateHandler(
        ControlledJobRepository repository,
        ControlledCompletionRepository? completion = null)
    {
        ProcessNextSimulationJobHandler handler = new(
            new ClaimSimulationJobHandler(repository),
            new RunSimulationHandler(),
            completion ?? new ControlledCompletionRepository(),
            new FailSimulationJobHandler(repository));
        return handler;
    }

    private static SimulationJobResult CreateRunningJob(Guid jobId)
    {
        return new SimulationJobResult(
            jobId,
            new RunSimulationRequest(2.5, 0.005, 0.1, 0.9, 4, SamplingMode.Uniform),
            SimulationJobStatus.Running,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);
    }

    private static SimulationJobResult CreateCompletedJob(Guid jobId, Guid simulationId)
    {
        SimulationJobResult running = CreateRunningJob(jobId);
        return running with
        {
            Status = SimulationJobStatus.Completed,
            SimulationId = simulationId,
            FinishedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class ControlledDelay : ISimulationJobWorkerDelay
    {
        private readonly Channel<DelayRequest> _channel =
            Channel.CreateUnbounded<DelayRequest>();

        public ConcurrentQueue<DelayRequest> Requests { get; } = new();

        public Func<bool>? DisposalProbe { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayRequest request = new(
                delay,
                cancellationToken,
                DisposalProbe?.Invoke());
            Requests.Enqueue(request);
            _channel.Writer.TryWrite(request);
            return request.Task;
        }

        public async Task<DelayRequest> ReadRequestAsync()
        {
            using CancellationTokenSource safetyCancellation = new(SafetyTimeout);
            return await _channel.Reader.ReadAsync(safetyCancellation.Token);
        }
    }

    private sealed class DelayRequest
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public DelayRequest(
            TimeSpan duration,
            CancellationToken cancellationToken,
            bool? scopeWasDisposed)
        {
            Duration = duration;
            CancellationToken = cancellationToken;
            ScopeWasDisposed = scopeWasDisposed;
            _registration = cancellationToken.Register(
                () => _completion.TrySetCanceled(cancellationToken));
        }

        public TimeSpan Duration { get; }
        public CancellationToken CancellationToken { get; }
        public bool? ScopeWasDisposed { get; }
        public Task Task => _completion.Task;

        public void Release()
        {
            _registration.Dispose();
            _completion.TrySetResult();
        }

        public void Fail(Exception exception)
        {
            _registration.Dispose();
            _completion.TrySetException(exception);
        }
    }

    private sealed class RecordingScopeFactory : IServiceScopeFactory
    {
        private readonly Func<int, RecordingScope> _factory;
        private readonly Action? _onCreate;

        public RecordingScopeFactory(
            Func<int, RecordingScope> factory,
            Action? onCreate = null)
        {
            _factory = factory;
            _onCreate = onCreate;
        }

        public int CreateCount { get; private set; }
        public List<RecordingScope> CreatedScopes { get; } = new();

        public IServiceScope CreateScope()
        {
            _onCreate?.Invoke();
            RecordingScope scope = _factory(CreateCount++);
            CreatedScopes.Add(scope);
            return scope;
        }
    }

    private sealed class RecordingScope : IServiceScope, IAsyncDisposable
    {
        private readonly Exception? _disposeException;

        public RecordingScope(
            ControlledServiceProvider provider,
            Exception? disposeException = null)
        {
            Provider = provider;
            _disposeException = disposeException;
        }

        public ControlledServiceProvider Provider { get; }
        public IServiceProvider ServiceProvider => Provider;
        public int AsyncDisposeCount { get; private set; }
        public int SyncDisposeCount { get; private set; }

        public void Dispose() => SyncDisposeCount++;

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return _disposeException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(_disposeException);
        }
    }

    private sealed class ControlledServiceProvider : IServiceProvider
    {
        private readonly ProcessNextSimulationJobHandler? _handler;
        private readonly Exception? _exception;

        public ControlledServiceProvider(
            ProcessNextSimulationJobHandler? handler = null,
            Exception? exception = null)
        {
            _handler = handler;
            _exception = exception;
        }

        public int ResolutionCount { get; private set; }
        public object? GetService(Type serviceType)
        {
            if (serviceType != typeof(ProcessNextSimulationJobHandler))
            {
                return null;
            }

            ResolutionCount++;
            if (_exception is not null)
            {
                throw _exception;
            }

            return _handler;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly Channel<LogEntry> _entries =
            Channel.CreateUnbounded<LogEntry>();

        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public LogLevel? ThrowOnLevel { get; init; }

        public Exception? LoggingException { get; init; }

        public int ThrowCount { get; private set; }

        public async Task<LogEntry> ReadEntryAsync()
        {
            using CancellationTokenSource safetyCancellation = new(SafetyTimeout);
            return await _entries.Reader.ReadAsync(safetyCancellation.Token);
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == ThrowOnLevel)
            {
                ThrowCount++;
                throw LoggingException ??
                    new InvalidOperationException("Configured logger failure.");
            }

            Dictionary<string, object?> properties = state is
                IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            LogEntry entry = new(
                logLevel,
                exception,
                formatter(state, exception),
                properties);
            Entries.Enqueue(entry);
            _entries.Writer.TryWrite(entry);
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class ControlledJobRepository : ISimulationJobRepository
    {
        private readonly TaskCompletionSource _claimStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SimulationJobResult? ClaimResult { get; set; }
        public Exception? ClaimException { get; set; }
        public bool BlockClaimUntilCancellation { get; set; }
        public CancellationToken ReceivedToken { get; private set; }

        public Task WaitForClaimAsync() =>
            WaitWithSafetyCancellationAsync(_claimStarted.Task);

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<SimulationJobResult?> ClaimNextPendingAsync(
            CancellationToken cancellationToken)
        {
            ReceivedToken = cancellationToken;
            _claimStarted.TrySetResult();
            if (ClaimException is not null)
            {
                throw ClaimException;
            }

            if (BlockClaimUntilCancellation)
            {
                TaskCompletionSource completion = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(
                        () => completion.TrySetCanceled(cancellationToken));
                await completion.Task;
            }

            return ClaimResult;
        }

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid simulationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            string failureMessage,
            CancellationToken cancellationToken) =>
            Task.FromResult<SimulationJobResult?>(null);
    }

    private sealed class ControlledCompletionRepository :
        ISimulationJobCompletionRepository
    {
        public SimulationJobResult? Result { get; set; }

        public Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
            Guid jobId,
            RunSimulationResult simulation,
            CancellationToken cancellationToken) => Task.FromResult(Result);
    }
}