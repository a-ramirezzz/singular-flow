using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.SimulationJobs;

public sealed class ProcessNextSimulationJobHandler
{
    private const string ExecutionFailureMessage =
        "Simulation execution failed.";

    private const string FinalizationFailureMessage =
        "Simulation persistence or completion failed.";

    private const string FailureTransitionDiagnosticKey =
        "SingularFlow.Application.SimulationJobs." +
        "ProcessNextSimulationJobHandler.FailureTransition";

    private readonly ClaimSimulationJobHandler _claimHandler;
    private readonly RunSimulationHandler _simulationHandler;
    private readonly ISimulationJobCompletionRepository
        _completionRepository;
    private readonly FailSimulationJobHandler _failHandler;

    public ProcessNextSimulationJobHandler(
        ClaimSimulationJobHandler claimHandler,
        RunSimulationHandler simulationHandler,
        ISimulationJobCompletionRepository completionRepository,
        FailSimulationJobHandler failHandler)
    {
        _claimHandler = claimHandler;
        _simulationHandler = simulationHandler;
        _completionRepository = completionRepository;
        _failHandler = failHandler;
    }

    public async Task<ProcessNextSimulationJobResult> HandleAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SimulationJobResult? claimedJob =
            await _claimHandler.HandleAsync(cancellationToken);

        if (claimedJob is null)
        {
            return new ProcessNextSimulationJobResult.NoWork();
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValidateClaimedJob(claimedJob);

        RunSimulationResult simulation;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            simulation = _simulationHandler.Handle(
                claimedJob.Request);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            await TryMarkFailedAsync(
                claimedJob.Id,
                ExecutionFailureMessage,
                cancellationToken,
                exception);
            throw;
        }

        SimulationJobResult? terminalJob;

        try
        {
            terminalJob =
                await _completionRepository.SaveSimulationAndCompleteAsync(
                    claimedJob.Id,
                    simulation,
                    cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            await TryMarkFailedAsync(
                claimedJob.Id,
                FinalizationFailureMessage,
                cancellationToken,
                exception);
            throw;
        }

        if (terminalJob is null)
        {
            throw new InvalidOperationException(
                "Atomic completion was rejected for the claimed job.");
        }

        ValidateCompletedJob(claimedJob.Id, terminalJob);

        return new ProcessNextSimulationJobResult.Completed(
            terminalJob.Id,
            terminalJob.SimulationId!.Value);
    }

    private async Task TryMarkFailedAsync(
        Guid jobId,
        string failureMessage,
        CancellationToken cancellationToken,
        Exception originalException)
    {
        try
        {
            SimulationJobResult? failedJob =
                await _failHandler.HandleAsync(
                    jobId,
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

    private static void ValidateClaimedJob(
        SimulationJobResult claimedJob)
    {
        if (claimedJob.Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has an empty ID.");
        }

        if (claimedJob.Status != SimulationJobStatus.Running)
        {
            throw new InvalidOperationException(
                "Claimed simulation job is not Running.");
        }

        if (claimedJob.ClaimedAtUtc is null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has no claim timestamp.");
        }

        if (claimedJob.SimulationId is not null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job already has a simulation ID.");
        }

        if (claimedJob.FinishedAtUtc is not null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job already has a finish timestamp.");
        }

        if (claimedJob.FailureMessage is not null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job already has a failure message.");
        }

        if (claimedJob.Request is null)
        {
            throw new InvalidOperationException(
                "Claimed simulation job has no simulation request.");
        }
    }

    private static void ValidateCompletedJob(
        Guid claimedJobId,
        SimulationJobResult terminalJob)
    {
        if (terminalJob.Id != claimedJobId)
        {
            throw new InvalidOperationException(
                "Completed simulation job ID does not match the claim.");
        }

        if (terminalJob.Status != SimulationJobStatus.Completed)
        {
            throw new InvalidOperationException(
                "Atomic completion returned a job that is not Completed.");
        }

        if (terminalJob.SimulationId is null ||
            terminalJob.SimulationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Completed simulation job has no valid simulation ID.");
        }

        if (terminalJob.FinishedAtUtc is null)
        {
            throw new InvalidOperationException(
                "Completed simulation job has no finish timestamp.");
        }

        if (terminalJob.FailureMessage is not null)
        {
            throw new InvalidOperationException(
                "Completed simulation job has a failure message.");
        }
    }
}