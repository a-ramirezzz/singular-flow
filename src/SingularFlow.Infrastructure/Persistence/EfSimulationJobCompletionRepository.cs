using System.Runtime.ExceptionServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationJobCompletionRepository :
    ISimulationJobCompletionRepository
{
    private const string CleanupExceptionKey =
        "SingularFlow.Infrastructure.SimulationJobCompletion.CleanupException";

    private readonly SingularFlowDbContext _context;

    public EfSimulationJobCompletionRepository(
        SingularFlowDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<SimulationJobResult?> SaveSimulationAndCompleteAsync(
        Guid jobId,
        RunSimulationResult simulation,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "A simulation job identifier is required.",
                nameof(jobId));
        }

        ArgumentNullException.ThrowIfNull(simulation);
        cancellationToken.ThrowIfCancellationRequested();

        if (_context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "The DbContext has pending tracked changes. " +
                "Save or discard them before completing a simulation job.");
        }

        HashSet<object> preexistingEntities =
            _context.ChangeTracker
                .Entries()
                .Select(entry => entry.Entity)
                .ToHashSet(ReferenceEqualityComparer.Instance);

        IDbContextTransaction? transaction =
            _context.Database.CurrentTransaction;
        IDbContextTransaction? ownedTransaction = null;
        string savepointName =
            $"simulation_job_completion_{Guid.NewGuid():N}";
        bool boundaryCreated = false;
        bool boundaryCompleted = false;
        ExceptionDispatchInfo? primaryException = null;
        SimulationJobResult? completedJob = null;

        try
        {
            if (transaction is null)
            {
                ownedTransaction =
                    await _context.Database.BeginTransactionAsync(
                        cancellationToken);
                transaction = ownedTransaction;
            }
            else
            {
                if (!transaction.SupportsSavepoints)
                {
                    throw new NotSupportedException(
                        "The current database transaction does not support savepoints.");
                }

                await transaction.CreateSavepointAsync(
                    savepointName,
                    cancellationToken);
            }

            boundaryCreated = true;

            PersistedSimulationResult persistedSimulation =
                await new EfSimulationRepository(_context).SaveAsync(
                    simulation,
                    cancellationToken);

            completedJob =
                await new EfSimulationJobRepository(_context)
                    .TryCompleteAsync(
                        jobId,
                        persistedSimulation.Id,
                        cancellationToken);

            if (completedJob is null)
            {
                await RollbackBoundaryAsync(
                    transaction,
                    ownedTransaction,
                    savepointName);
                boundaryCompleted = true;
            }
            else if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
                boundaryCompleted = true;
            }
            else
            {
                await transaction.ReleaseSavepointAsync(
                    savepointName,
                    cancellationToken);
                boundaryCompleted = true;
            }
        }
        catch (Exception exception)
        {
            primaryException = ExceptionDispatchInfo.Capture(exception);
        }

        if (primaryException is not null &&
            boundaryCreated &&
            !boundaryCompleted)
        {
            try
            {
                await RollbackBoundaryAsync(
                    transaction!,
                    ownedTransaction,
                    savepointName);
                boundaryCompleted = true;
            }
            catch (Exception exception)
            {
                AddCleanupException(ref primaryException, exception);
            }
        }

        if (primaryException is not null || completedJob is null)
        {
            try
            {
                DetachAttemptEntities(preexistingEntities);
            }
            catch (Exception exception)
            {
                AddCleanupException(ref primaryException, exception);
            }
        }

        if (ownedTransaction is not null)
        {
            try
            {
                await ownedTransaction.DisposeAsync();
            }
            catch (Exception exception)
            {
                AddCleanupException(ref primaryException, exception);
            }
        }

        primaryException?.Throw();
        return completedJob;
    }

    private static async Task RollbackBoundaryAsync(
        IDbContextTransaction transaction,
        IDbContextTransaction? ownedTransaction,
        string savepointName)
    {
        if (ownedTransaction is not null)
        {
            await ownedTransaction.RollbackAsync(CancellationToken.None);
            return;
        }

        await transaction.RollbackToSavepointAsync(
            savepointName,
            CancellationToken.None);
        await transaction.ReleaseSavepointAsync(
            savepointName,
            CancellationToken.None);
    }

    private void DetachAttemptEntities(
        HashSet<object> preexistingEntities)
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry
            in _context.ChangeTracker.Entries().ToArray())
        {
            if (!preexistingEntities.Contains(entry.Entity))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static void AddCleanupException(
        ref ExceptionDispatchInfo? primaryException,
        Exception cleanupException)
    {
        if (primaryException is null)
        {
            primaryException = ExceptionDispatchInfo.Capture(cleanupException);
            return;
        }

        primaryException.SourceException.Data[CleanupExceptionKey] =
            cleanupException;
    }
}