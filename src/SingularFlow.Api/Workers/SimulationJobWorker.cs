using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SingularFlow.Application.SimulationJobs;

namespace SingularFlow.Api.Workers;

internal sealed class SimulationJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SimulationJobWorkerOptions _options;
    private readonly ISimulationJobWorkerDelay _delay;
    private readonly SimulationJobLeaseSupervisor _leaseSupervisor;
    private readonly ILogger<SimulationJobWorker> _logger;

    public SimulationJobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SimulationJobWorkerOptions> options,
        ISimulationJobWorkerDelay delay,
        SimulationJobLeaseSupervisor leaseSupervisor,
        ILogger<SimulationJobWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(leaseSupervisor);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _delay = delay;
        _leaseSupervisor = leaseSupervisor;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            await base.StartAsync(cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await ValidateStartupAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Simulation job worker is disabled.");
            return;
        }

        _logger.LogInformation(
            "Simulation job worker started. PollingInterval={PollingInterval}, FailureDelay={FailureDelay}, LeaseDuration={LeaseDuration}, HeartbeatInterval={HeartbeatInterval}, RecoveryInterval={RecoveryInterval}, RecoveryBatchSize={RecoveryBatchSize}",
            _options.PollingInterval,
            _options.FailureDelay,
            _options.LeaseDuration,
            _options.HeartbeatInterval,
            _options.RecoveryInterval,
            _options.RecoveryBatchSize);

        try
        {
            using CancellationTokenSource loopsCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Task processingTask = ProcessingLoopAsync(loopsCancellation.Token);
            Task recoveryTask = RecoveryLoopAsync(loopsCancellation.Token);
            Task first = await Task.WhenAny(processingTask, recoveryTask);

            if (!first.IsCompletedSuccessfully)
            {
                ExceptionDispatchInfo primary = await CaptureFailureAsync(first);
                loopsCancellation.Cancel();
                Task sibling = ReferenceEquals(first, processingTask)
                    ? recoveryTask
                    : processingTask;
                await ObserveSecondaryAsync(sibling);
                primary.Throw();
            }

            await Task.WhenAll(processingTask, recoveryTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation("Simulation job worker stopping.");
        }
    }

    private async Task ProcessingLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool failed = false;
            SimulationJobClaim? claim = null;
            try
            {
                claim = await ClaimSingleJobAsync(stoppingToken);

                if (claim is not null)
                {
                    ProcessClaimedSimulationJobResult result = ValidateResult(
                        await _leaseSupervisor.ProcessAsync(
                            claim,
                            _options.LeaseDuration,
                            _options.HeartbeatInterval,
                            stoppingToken));

                    switch (result)
                    {
                        case ProcessClaimedSimulationJobResult.Completed completed:
                            _logger.LogInformation(
                                "Simulation job completed. JobId={JobId}, SimulationId={SimulationId}",
                                completed.JobId,
                                completed.SimulationId);
                            break;
                        case ProcessClaimedSimulationJobResult.LeaseLost leaseLost:
                            _logger.LogWarning(
                                "Simulation job lease ownership was lost. JobId={JobId}",
                                leaseLost.JobId);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                failed = true;
                _logger.LogError(
                    exception,
                    "Simulation job processing loop iteration failed.");
            }

            if (failed)
            {
                await _delay.DelayAsync(_options.FailureDelay, stoppingToken);
            }
            else if (claim is null)
            {
                await _delay.DelayAsync(_options.PollingInterval, stoppingToken);
            }
        }
    }

    private async Task RecoveryLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool failed = false;
            try
            {
                IReadOnlyList<Guid> recovered =
                    await RecoverSingleBatchAsync(stoppingToken);
                if (recovered.Count > 0)
                {
                    _logger.LogInformation(
                        "Expired simulation jobs recovered. RecoveredCount={RecoveredCount}",
                        recovered.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                failed = true;
                _logger.LogError(
                    exception,
                    "Simulation job recovery loop iteration failed.");
            }

            await _delay.DelayAsync(
                failed ? _options.FailureDelay : _options.RecoveryInterval,
                stoppingToken);
        }
    }

    private async Task ValidateStartupAsync(CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ExceptionDispatchInfo? primary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IServiceProvider services = scope.ServiceProvider;
            _ = services.GetRequiredService<ClaimSimulationJobLeaseHandler>();
            _ = services.GetRequiredService<ProcessClaimedSimulationJobHandler>();
            _ = services.GetRequiredService<RenewSimulationJobLeaseHandler>();
            _ = services.GetRequiredService<RecoverExpiredSimulationJobsHandler>();
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        await DisposeScopeAsync(scope, primary);
        primary?.Throw();
    }

    private async Task<SimulationJobClaim?> ClaimSingleJobAsync(
        CancellationToken stoppingToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ExceptionDispatchInfo? primary = null;
        SimulationJobClaim? result = null;
        try
        {
            ClaimSimulationJobLeaseHandler handler = scope.ServiceProvider
                .GetRequiredService<ClaimSimulationJobLeaseHandler>();
            result = await handler.HandleAsync(_options.LeaseDuration, stoppingToken);
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        await DisposeScopeAsync(scope, primary);
        primary?.Throw();
        return result;
    }

    private async Task<IReadOnlyList<Guid>> RecoverSingleBatchAsync(
        CancellationToken stoppingToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ExceptionDispatchInfo? primary = null;
        IReadOnlyList<Guid>? result = null;
        try
        {
            RecoverExpiredSimulationJobsHandler handler = scope.ServiceProvider
                .GetRequiredService<RecoverExpiredSimulationJobsHandler>();
            result = await handler.HandleAsync(_options.RecoveryBatchSize, stoppingToken);
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        await DisposeScopeAsync(scope, primary);
        primary?.Throw();
        return result ?? throw new InvalidOperationException(
            "Simulation job recovery returned a null result.");
    }

    private async Task DisposeScopeAsync(
        AsyncServiceScope scope,
        ExceptionDispatchInfo? primary)
    {
        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception exception)
        {
            if (primary is null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }

            TryLogSecondaryScopeDisposalFailure(exception);
        }
    }

    internal static ProcessClaimedSimulationJobResult ValidateResult(
        ProcessClaimedSimulationJobResult? result) => result switch
        {
            ProcessClaimedSimulationJobResult.Completed => result,
            ProcessClaimedSimulationJobResult.LeaseLost => result,
            null => throw new InvalidOperationException(
                "Simulation job worker returned a null processing result."),
            _ => throw new InvalidOperationException(
                "Simulation job worker returned an unsupported processing result.")
        };

    private static async Task<ExceptionDispatchInfo> CaptureFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            return ExceptionDispatchInfo.Capture(exception);
        }

        throw new InvalidOperationException(
            "A simulation job worker loop ended unexpectedly.");
    }

    private async Task ObserveSecondaryAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            try
            {
                _logger.LogWarning(
                    exception,
                    "Secondary simulation job worker loop failure.");
            }
            catch
            {
                // Preserve the already captured primary loop failure.
            }
        }
    }

    private void TryLogSecondaryScopeDisposalFailure(Exception exception)
    {
        try
        {
            _logger.LogWarning(
                exception,
                "Secondary simulation job worker scope disposal failed.");
        }
        catch
        {
            // Preserve the already captured primary exception.
        }
    }
}