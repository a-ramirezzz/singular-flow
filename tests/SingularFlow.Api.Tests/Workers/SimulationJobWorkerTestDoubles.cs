using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Workers;

internal sealed class SimulationJobProcessingProbe
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);
    private readonly TaskCompletionSource<CancellationToken> _claimObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ClaimCount { get; private set; }

    public int FailureCount { get; private set; }

    public int CompletionCount { get; private set; }

    public void RecordClaim(CancellationToken cancellationToken)
    {
        ClaimCount++;
        _claimObserved.TrySetResult(cancellationToken);
    }

    public void RecordFailure()
    {
        FailureCount++;
    }

    public void RecordCompletion()
    {
        CompletionCount++;
    }

    public async Task<CancellationToken> WaitForClaimAsync()
    {
        using CancellationTokenSource safetyCancellation =
            new(SafetyTimeout);
        return await _claimObserved.Task.WaitAsync(
            safetyCancellation.Token);
    }
}

internal sealed class ControlledSimulationJobRepository :
    ISimulationJobRepository
{
    private readonly SimulationJobProcessingProbe _probe;

    public ControlledSimulationJobRepository(
        SimulationJobProcessingProbe probe)
    {
        _probe = probe;
    }

    public Task<SimulationJobResult?> ClaimNextPendingAsync(
        CancellationToken cancellationToken)
    {
        _probe.RecordClaim(cancellationToken);
        return Task.FromResult<SimulationJobResult?>(null);
    }

    public Task<SimulationJobResult> CreateAsync(
        RunSimulationRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<SimulationJobResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<SimulationJobResult?> TryCompleteAsync(
        Guid jobId,
        Guid simulationId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<SimulationJobResult?> TryFailAsync(
        Guid jobId,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        _probe.RecordFailure();
        throw new InvalidOperationException(
            "The controlled worker must not fail a job.");
    }
}

internal sealed class ControlledSimulationJobCompletionRepository :
    ISimulationJobCompletionRepository
{
    private readonly SimulationJobProcessingProbe _probe;

    public ControlledSimulationJobCompletionRepository(
        SimulationJobProcessingProbe probe)
    {
        _probe = probe;
    }

    public Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
        Guid jobId,
        RunSimulationResult simulation,
        CancellationToken cancellationToken)
    {
        _probe.RecordCompletion();
        throw new InvalidOperationException(
            "The controlled worker must not complete a job.");
    }
}