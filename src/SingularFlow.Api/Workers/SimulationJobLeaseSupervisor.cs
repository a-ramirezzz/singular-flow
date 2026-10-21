using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SingularFlow.Application.SimulationJobs;

namespace SingularFlow.Api.Workers;

internal sealed class SimulationJobLeaseSupervisor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISimulationJobWorkerDelay _delay;
    private readonly ILogger<SimulationJobLeaseSupervisor> _logger;

    public SimulationJobLeaseSupervisor(
        IServiceScopeFactory scopeFactory,
        ISimulationJobWorkerDelay delay,
        ILogger<SimulationJobLeaseSupervisor> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _delay = delay;
        _logger = logger;
    }

    public async Task<ProcessClaimedSimulationJobResult> ProcessAsync(
        SimulationJobClaim claim,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        if (heartbeatInterval <= TimeSpan.Zero ||
            heartbeatInterval >= leaseDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));
        }

        cancellationToken.ThrowIfCancellationRequested();
        claim.Validate();

        using CancellationTokenSource processingCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource heartbeatCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        AsyncServiceScope processingScope = _scopeFactory.CreateAsyncScope();
        Task<ProcessClaimedSimulationJobResult> processingTask =
            ProcessInScopeAsync(
                processingScope,
                claim,
                processingCancellation.Token);
        Task<HeartbeatOutcome> heartbeatTask = HeartbeatAsync(
            claim,
            leaseDuration,
            heartbeatInterval,
            heartbeatCancellation.Token);

        Task first = await Task.WhenAny(processingTask, heartbeatTask);

        if (first == processingTask || processingTask.IsCompleted)
        {
            return await FinishAfterProcessingAsync(
                processingTask,
                heartbeatTask,
                heartbeatCancellation,
                cancellationToken,
                claim.Job.Id);
        }

        return await FinishAfterHeartbeatAsync(
            processingTask,
            heartbeatTask,
            processingCancellation,
            cancellationToken,
            claim.Job.Id);
    }

    private async Task<ProcessClaimedSimulationJobResult>
        FinishAfterProcessingAsync(
            Task<ProcessClaimedSimulationJobResult> processingTask,
            Task<HeartbeatOutcome> heartbeatTask,
            CancellationTokenSource heartbeatCancellation,
            CancellationToken externalToken,
            Guid jobId)
    {
        ExceptionDispatchInfo? processingFailure = null;
        ProcessClaimedSimulationJobResult? result = null;

        try
        {
            result = await processingTask;
        }
        catch (Exception exception)
        {
            processingFailure = ExceptionDispatchInfo.Capture(exception);
        }

        heartbeatCancellation.Cancel();

        try
        {
            _ = await heartbeatTask;
        }
        catch (OperationCanceledException) when (
            heartbeatCancellation.IsCancellationRequested &&
            !externalToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TryLogSecondaryFailure(
                exception,
                jobId,
                "HeartbeatCleanup");
        }

        if (externalToken.IsCancellationRequested &&
            processingFailure?.SourceException is OperationCanceledException)
        {
            externalToken.ThrowIfCancellationRequested();
        }

        processingFailure?.Throw();
        return result!;
    }

    private async Task<ProcessClaimedSimulationJobResult>
        FinishAfterHeartbeatAsync(
            Task<ProcessClaimedSimulationJobResult> processingTask,
            Task<HeartbeatOutcome> heartbeatTask,
            CancellationTokenSource processingCancellation,
            CancellationToken externalToken,
            Guid jobId)
    {
        ExceptionDispatchInfo? heartbeatFailure = null;
        HeartbeatOutcome heartbeatOutcome = HeartbeatOutcome.Running;

        try
        {
            heartbeatOutcome = await heartbeatTask;
        }
        catch (Exception exception)
        {
            heartbeatFailure = ExceptionDispatchInfo.Capture(exception);
        }

        processingCancellation.Cancel();

        ExceptionDispatchInfo? processingFailure = null;
        ProcessClaimedSimulationJobResult? processingResult = null;

        try
        {
            processingResult = await processingTask;
        }
        catch (Exception exception)
        {
            processingFailure = ExceptionDispatchInfo.Capture(exception);
        }

        if (processingResult is ProcessClaimedSimulationJobResult.Completed)
        {
            if (heartbeatFailure is not null)
            {
                TryLogSecondaryFailure(
                    heartbeatFailure.SourceException,
                    jobId,
                    "HeartbeatRace");
            }

            return processingResult;
        }

        if (externalToken.IsCancellationRequested)
        {
            externalToken.ThrowIfCancellationRequested();
        }

        if (heartbeatFailure is not null)
        {
            if (processingFailure is not null &&
                processingFailure.SourceException is not OperationCanceledException)
            {
                TryLogSecondaryFailure(
                    processingFailure.SourceException,
                    jobId,
                    "ProcessingCancellation");
            }

            heartbeatFailure.Throw();
        }

        if (heartbeatOutcome == HeartbeatOutcome.LeaseLost)
        {
            if (processingResult is ProcessClaimedSimulationJobResult.LeaseLost)
            {
                return processingResult;
            }

            if (processingFailure?.SourceException is OperationCanceledException &&
                processingCancellation.IsCancellationRequested)
            {
                return new ProcessClaimedSimulationJobResult.LeaseLost(jobId);
            }

            processingFailure?.Throw();
            return new ProcessClaimedSimulationJobResult.LeaseLost(jobId);
        }

        processingFailure?.Throw();
        throw new InvalidOperationException(
            "Simulation job heartbeat ended without an outcome.");
    }

    private async Task<ProcessClaimedSimulationJobResult> ProcessInScopeAsync(
        AsyncServiceScope scope,
        SimulationJobClaim claim,
        CancellationToken cancellationToken)
    {
        ExceptionDispatchInfo? primaryFailure = null;
        ProcessClaimedSimulationJobResult? result = null;

        try
        {
            ProcessClaimedSimulationJobHandler handler =
                scope.ServiceProvider
                    .GetRequiredService<ProcessClaimedSimulationJobHandler>();
            result = await handler.HandleAsync(claim, cancellationToken);
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception exception)
        {
            if (primaryFailure is not null ||
                result is ProcessClaimedSimulationJobResult.Completed)
            {
                TryLogSecondaryFailure(
                    exception,
                    claim.Job.Id,
                    "ProcessingScopeDisposal");
            }
            else
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
        }

        primaryFailure?.Throw();
        return result!;
    }

    private async Task<HeartbeatOutcome> HeartbeatAsync(
        SimulationJobClaim claim,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await _delay.DelayAsync(
                heartbeatInterval,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset? renewed = await RenewInFreshScopeAsync(
                claim,
                leaseDuration,
                cancellationToken);

            if (renewed is null)
            {
                TryLogLeaseLost(claim.Job.Id);
                return HeartbeatOutcome.LeaseLost;
            }
        }
    }

    private async Task<DateTimeOffset?> RenewInFreshScopeAsync(
        SimulationJobClaim claim,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ExceptionDispatchInfo? primaryFailure = null;
        DateTimeOffset? result = null;

        try
        {
            RenewSimulationJobLeaseHandler handler =
                scope.ServiceProvider
                    .GetRequiredService<RenewSimulationJobLeaseHandler>();
            result = await handler.HandleAsync(
                claim.Job.Id,
                claim.ClaimToken,
                leaseDuration,
                cancellationToken);
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception exception)
        {
            if (primaryFailure is null)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
            else
            {
                TryLogSecondaryFailure(
                    exception,
                    claim.Job.Id,
                    "RenewalScopeDisposal");
            }
        }

        primaryFailure?.Throw();
        return result;
    }

    private void TryLogLeaseLost(Guid jobId)
    {
        try
        {
            _logger.LogWarning(
                "Simulation job lease ownership was lost while processing. JobId={JobId}",
                jobId);
        }
        catch
        {
            // Logging must not replace the supervision outcome.
        }
    }

    private void TryLogSecondaryFailure(
        Exception exception,
        Guid jobId,
        string context)
    {
        try
        {
            _logger.LogWarning(
                exception,
                "Secondary simulation job supervision failure. JobId={JobId}, Context={ExceptionContext}",
                jobId,
                context);
        }
        catch
        {
            // Preserve the already selected primary outcome.
        }
    }

    private enum HeartbeatOutcome
    {
        Running,
        LeaseLost
    }
}