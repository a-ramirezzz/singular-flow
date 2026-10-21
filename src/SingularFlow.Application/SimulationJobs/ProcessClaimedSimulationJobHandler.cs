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

    public ProcessClaimedSimulationJobHandler(
        RunSimulationHandler runSimulationHandler,
        ISimulationJobCompletionRepository completionRepository,
        FailSimulationJobHandler failHandler)
    {
        _runSimulationHandler = runSimulationHandler;
        _completionRepository = completionRepository;
        _failHandler = failHandler;
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
            await TryMarkFailedAsync(
                claim,
                ExecutionFailureMessage,
                cancellationToken,
                exception);
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
            await TryMarkFailedAsync(
                claim,
                FinalizationFailureMessage,
                cancellationToken,
                exception);
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
}