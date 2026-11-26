using System.Runtime.ExceptionServices;

using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.SimulationJobs;

public sealed class ProcessClaimedSimulationJobHandler
{
    private const string ExecutionFailureMessage =
        "Simulation execution failed.";

    private const string FinalizationFailureMessage =
        "Simulation persistence or completion failed.";

    private const string FailureTransitionDiagnosticKey =
        "SingularFlow.Application.SimulationJobs." +
        "ProcessClaimedSimulationJobHandler.FailureTransition";

    private readonly RunSimulationHandler _runSimulationHandler;
    private readonly ISimulationJobCompletionRepository
        _completionRepository;
    private readonly FailSimulationJobHandler _failHandler;
    private readonly ISimulationJobFailureClassifier _failureClassifier;
    private readonly ISimulationJobRetryRepository _retryRepository;
    private readonly SimulationJobRetryPolicy _retryPolicy;

    public ProcessClaimedSimulationJobHandler(
        RunSimulationHandler runSimulationHandler,
        ISimulationJobCompletionRepository completionRepository,
        FailSimulationJobHandler failHandler,
        ISimulationJobFailureClassifier failureClassifier,
        ISimulationJobRetryRepository retryRepository,
        SimulationJobRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(runSimulationHandler);
        ArgumentNullException.ThrowIfNull(completionRepository);
        ArgumentNullException.ThrowIfNull(failHandler);
        ArgumentNullException.ThrowIfNull(failureClassifier);
        ArgumentNullException.ThrowIfNull(retryRepository);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        _runSimulationHandler = runSimulationHandler;
        _completionRepository = completionRepository;
        _failHandler = failHandler;
        _failureClassifier = failureClassifier;
        _retryRepository = retryRepository;
        _retryPolicy = retryPolicy;
    }

    public async Task<ProcessClaimedSimulationJobResult> HandleAsync(
        SimulationJobClaim claim,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(claim);
        claim.Validate();

        RunSimulationResult simulationResult;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            simulationResult = _runSimulationHandler.Handle(
                claim.Job.Request);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            ProcessClaimedSimulationJobResult? failureResult =
                await HandleFailureAsync(
                claim,
                ExecutionFailureMessage,
                SimulationJobFailurePhase.Execution,
                cancellationToken,
                exception);
            if (failureResult is not null)
            {
                return failureResult;
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        SimulationJobResult? completedJob;

        try
        {
            completedJob =
                await _completionRepository.SaveSimulationAndCompleteAsync(
                    claim.Job.Id,
                    claim.ClaimToken,
                    simulationResult,
                    cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            ProcessClaimedSimulationJobResult? failureResult =
                await HandleFailureAsync(
                claim,
                FinalizationFailureMessage,
                SimulationJobFailurePhase.Persistence,
                cancellationToken,
                exception);
            if (failureResult is not null)
            {
                return failureResult;
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        if (completedJob is null)
        {
            return new ProcessClaimedSimulationJobResult.LeaseLost(
                claim.Job.Id);
        }

        ValidateCompletedJob(claim.Job.Id, completedJob);

        return new ProcessClaimedSimulationJobResult.Completed(
            completedJob.Id,
            completedJob.SimulationId!.Value);
    }

    private async Task<ProcessClaimedSimulationJobResult?>
        HandleFailureAsync(
            SimulationJobClaim claim,
            string failureMessage,
            SimulationJobFailurePhase phase,
            CancellationToken cancellationToken,
            Exception originalException)
    {
        SimulationJobFailureClassification classification;

        try
        {
            classification = _failureClassifier.Classify(
                originalException,
                phase);
        }
        catch (Exception)
        {
            await TryMarkFailedAsync(
                claim,
                failureMessage,
                cancellationToken,
                originalException);
            return null;
        }

        if (classification ==
            SimulationJobFailureClassification.NonRetryable)
        {
            await TryMarkFailedAsync(
                claim,
                failureMessage,
                cancellationToken,
                originalException);
            return null;
        }

        SimulationJobRetryDecision? decision;

        try
        {
            TimeSpan retryDelay = _retryPolicy.GetDelayAfterAttempt(
                claim.Job.AttemptCount);
            decision =
                await _retryRepository.TryRecordRetryableFailureAsync(
                    claim.Job.Id,
                    claim.ClaimToken,
                    failureMessage,
                    retryDelay,
                    cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        if (decision is null)
        {
            return new ProcessClaimedSimulationJobResult.LeaseLost(
                claim.Job.Id);
        }

        return decision switch
        {
            SimulationJobRetryDecision.Scheduled scheduled =>
                CreateScheduledResult(claim, scheduled.Job),
            SimulationJobRetryDecision.Exhausted exhausted =>
                CreateExhaustedResult(claim, exhausted.Job),
            _ => throw new InvalidOperationException(
                "Retry persistence returned an unknown decision.")
        };
    }

    private async Task TryMarkFailedAsync(
        SimulationJobClaim claim,
        string failureMessage,
        CancellationToken cancellationToken,
        Exception originalException)
    {
        try
        {
            SimulationJobResult? failedJob =
                await _failHandler.HandleAsync(
                    claim.Job.Id,
                    claim.ClaimToken,
                    failureMessage,
                    cancellationToken);

            if (failedJob is null)
            {
                originalException.Data[
                    FailureTransitionDiagnosticKey] =
                    "Failure transition returned no result.";
            }
        }
        catch (Exception failureException)
        {
            originalException.Data[
                FailureTransitionDiagnosticKey] =
                failureException;
        }
    }

    private static void ValidateCompletedJob(
        Guid claimedJobId,
        SimulationJobResult completedJob)
    {
        if (completedJob.Id != claimedJobId)
        {
            throw new InvalidOperationException(
                "Completed simulation job ID does not match the claim.");
        }

        if (completedJob.Status != SimulationJobStatus.Completed)
        {
            throw new InvalidOperationException(
                "Atomic completion returned a job that is not Completed.");
        }

        if (completedJob.SimulationId is null ||
            completedJob.SimulationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Completed simulation job has no valid simulation ID.");
        }

        if (completedJob.FinishedAtUtc is null)
        {
            throw new InvalidOperationException(
                "Completed simulation job has no finish timestamp.");
        }

        if (completedJob.FailureMessage is not null)
        {
            throw new InvalidOperationException(
                "Completed simulation job has a failure message.");
        }

        if (completedJob.ClaimedAtUtc is null)
        {
            throw new InvalidOperationException(
                "Completed simulation job has no claim timestamp.");
        }
    }

    private static ProcessClaimedSimulationJobResult CreateScheduledResult(
        SimulationJobClaim claim,
        SimulationJobResult job)
    {
        if (job.Id != claim.Job.Id)
        {
            throw new InvalidOperationException(
                "Scheduled retry job ID does not match the claim.");
        }

        if (job.Status != SimulationJobStatus.Pending)
        {
            throw new InvalidOperationException(
                "Scheduled retry returned a job that is not Pending.");
        }

        if (job.ClaimedAtUtc is not null)
        {
            throw new InvalidOperationException(
                "Scheduled retry job still has a claim timestamp.");
        }

        if (job.FinishedAtUtc is not null ||
            job.SimulationId is not null ||
            job.FailureMessage is not null)
        {
            throw new InvalidOperationException(
                "Scheduled retry job has terminal metadata.");
        }

        if (job.NextAttemptAtUtc is null)
        {
            throw new InvalidOperationException(
                "Scheduled retry job has no next-attempt timestamp.");
        }

        ValidateUnchangedAttempts(claim, job);

        if (job.AttemptCount >= job.MaxAttempts)
        {
            throw new InvalidOperationException(
                "Scheduled retry job has exhausted its attempts.");
        }

        ValidateIntermediateFailure(job, "Scheduled retry");

        return new ProcessClaimedSimulationJobResult.RetryScheduled(
            job.Id,
            job.NextAttemptAtUtc.Value);
    }

    private static ProcessClaimedSimulationJobResult CreateExhaustedResult(
        SimulationJobClaim claim,
        SimulationJobResult job)
    {
        if (job.Id != claim.Job.Id)
        {
            throw new InvalidOperationException(
                "Exhausted retry job ID does not match the claim.");
        }

        if (job.Status != SimulationJobStatus.Failed)
        {
            throw new InvalidOperationException(
                "Exhausted retry returned a job that is not Failed.");
        }

        if (job.ClaimedAtUtc is null || job.FinishedAtUtc is null)
        {
            throw new InvalidOperationException(
                "Exhausted retry job has incomplete terminal timestamps.");
        }

        if (job.SimulationId is not null ||
            string.IsNullOrWhiteSpace(job.FailureMessage))
        {
            throw new InvalidOperationException(
                "Exhausted retry job has invalid terminal metadata.");
        }

        if (job.NextAttemptAtUtc is not null)
        {
            throw new InvalidOperationException(
                "Exhausted retry job still has a next-attempt timestamp.");
        }

        ValidateUnchangedAttempts(claim, job);

        if (job.AttemptCount != job.MaxAttempts)
        {
            throw new InvalidOperationException(
                "Exhausted retry job has remaining attempts.");
        }

        ValidateIntermediateFailure(job, "Exhausted retry");

        return new ProcessClaimedSimulationJobResult.Failed(job.Id);
    }

    private static void ValidateUnchangedAttempts(
        SimulationJobClaim claim,
        SimulationJobResult job)
    {
        if (job.AttemptCount != claim.Job.AttemptCount)
        {
            throw new InvalidOperationException(
                "Retry persistence changed the attempt count.");
        }

        if (job.MaxAttempts != claim.Job.MaxAttempts)
        {
            throw new InvalidOperationException(
                "Retry persistence changed the maximum attempts.");
        }
    }

    private static void ValidateIntermediateFailure(
        SimulationJobResult job,
        string decisionName)
    {
        if (job.LastFailedAtUtc is null ||
            string.IsNullOrWhiteSpace(job.LastFailureMessage))
        {
            throw new InvalidOperationException(
                $"{decisionName} job has invalid intermediate-failure metadata.");
        }
    }
}