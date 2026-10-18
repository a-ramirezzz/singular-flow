using System.Diagnostics;
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
    private readonly ILogger<SimulationJobWorker> _logger;

    public SimulationJobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SimulationJobWorkerOptions> options,
        ISimulationJobWorkerDelay delay,
        ILogger<SimulationJobWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _delay = delay;
        _logger = logger;
    }

    public override async Task StartAsync(
        CancellationToken cancellationToken)
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

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Simulation job worker is disabled.");
            return;
        }

        _logger.LogInformation(
            "Simulation job worker started. PollingInterval={PollingInterval}, FailureDelay={FailureDelay}",
            _options.PollingInterval,
            _options.FailureDelay);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ProcessNextSimulationJobResult? result = null;
                Exception? iterationFailure = null;

                try
                {
                    result = ValidateResult(
                        await ProcessSingleIterationAsync(stoppingToken));
                }
                catch (OperationCanceledException) when (
                    stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    iterationFailure = exception;
                    _logger.LogError(
                        exception,
                        "Simulation job worker iteration failed.");
                }

                if (iterationFailure is not null)
                {
                    if (!await DelayForNextIterationAsync(
                        _options.FailureDelay,
                        stoppingToken))
                    {
                        break;
                    }

                    continue;
                }

                if (result is ProcessNextSimulationJobResult.NoWork)
                {
                    if (!await DelayForNextIterationAsync(
                        _options.PollingInterval,
                        stoppingToken))
                    {
                        break;
                    }

                    continue;
                }

                if (result is ProcessNextSimulationJobResult.Completed completed)
                {
                    _logger.LogInformation(
                        "Simulation job completed. JobId={JobId}, SimulationId={SimulationId}",
                        completed.JobId,
                        completed.SimulationId);
                    continue;
                }

                throw new UnreachableException();
            }
        }
        finally
        {
            _logger.LogInformation("Simulation job worker stopping.");
        }
    }

    private async Task ValidateStartupAsync(CancellationToken cancellationToken)
    {
        AsyncServiceScope validationScope =
            _scopeFactory.CreateAsyncScope();
        ExceptionDispatchInfo? primaryException = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = validationScope.ServiceProvider
                .GetRequiredService<ProcessNextSimulationJobHandler>();
        }
        catch (Exception exception)
        {
            primaryException = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await validationScope.DisposeAsync();
        }
        catch (Exception exception)
        {
            if (primaryException is null)
            {
                primaryException = ExceptionDispatchInfo.Capture(exception);
            }
            else
            {
                TryLogSecondaryScopeDisposalFailure(exception);
            }
        }

        primaryException?.Throw();
    }

    internal static ProcessNextSimulationJobResult ValidateResult(
        ProcessNextSimulationJobResult? result)
    {
        return result switch
        {
            ProcessNextSimulationJobResult.NoWork => result,
            ProcessNextSimulationJobResult.Completed => result,
            null => throw new InvalidOperationException(
                "Simulation job worker returned a null processing result."),
            _ => throw new InvalidOperationException(
                "Simulation job worker returned an unsupported processing result.")
        };
    }

    private async Task<ProcessNextSimulationJobResult?>
        ProcessSingleIterationAsync(
            CancellationToken stoppingToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ExceptionDispatchInfo? primaryException = null;
        ProcessNextSimulationJobResult? result = null;

        try
        {
            ProcessNextSimulationJobHandler handler =
                scope.ServiceProvider
                    .GetRequiredService<ProcessNextSimulationJobHandler>();

            result = await handler.HandleAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            primaryException = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception exception)
        {
            if (primaryException is null)
            {
                primaryException = ExceptionDispatchInfo.Capture(exception);
            }
            else
            {
                TryLogSecondaryScopeDisposalFailure(exception);
            }
        }

        if (primaryException is not null)
        {
            primaryException.Throw();
        }

        return result;
    }

    private void TryLogSecondaryScopeDisposalFailure(
        Exception disposalException)
    {
        try
        {
            _logger.LogWarning(
                disposalException,
                "Secondary simulation job worker scope disposal failed.");
        }
        catch
        {
            // Preserve the already captured primary exception.
        }
    }

    private async Task<bool> DelayForNextIterationAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _delay.DelayAsync(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}