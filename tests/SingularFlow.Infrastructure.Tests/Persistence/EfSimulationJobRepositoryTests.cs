using System.Data;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobRepositoryTests
{
    [Fact]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNullException()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .Options;

        await using SingularFlowDbContext context =
            new(options);

        EfSimulationJobRepository repository = new(
            context);

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repository.CreateAsync(
                    null!,
                    CancellationToken.None));

        Assert.Equal(
            "request",
            exception.ParamName);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndRoundTripsPendingJob()
    {
        string connectionString = GetTestConnectionString();

        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        RunSimulationRequest request = new(
            SingularTime: 2.5,
            ConcentrationExponent: 0.004,
            StartTime: 0.25,
            EndTime: 1.75,
            SampleCount: 17,
            SamplingMode: SamplingMode.Logarithmic);

        EfSimulationJobRepository repository = new(
            context);

        int jobCount = await context.SimulationJobs.CountAsync();
        int simulationCount = await context.Simulations.CountAsync();
        int stateCount = await context.SimulationStates.CountAsync();

        SimulationJobResult created =
            await repository.CreateAsync(
                request,
                CancellationToken.None);

        Assert.Equal(
            jobCount + 1,
            await context.SimulationJobs.CountAsync());
        Assert.Equal(
            simulationCount,
            await context.Simulations.CountAsync());
        Assert.Equal(
            stateCount,
            await context.SimulationStates.CountAsync());

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(default, created.CreatedAtUtc);
        Assert.Equal(SimulationJobStatus.Pending, created.Status);
        Assert.Null(created.ClaimedAtUtc);
        Assert.Equal(request, created.Request);

        SimulationJobEntity saved =
            await context.SimulationJobs
                .AsNoTracking()
                .SingleAsync(job => job.Id == created.Id);

        Assert.Equal(request.SingularTime, saved.SingularTime);
        Assert.Equal(
            request.ConcentrationExponent,
            saved.ConcentrationExponent);
        Assert.Equal(request.StartTime, saved.StartTime);
        Assert.Equal(request.EndTime, saved.EndTime);
        Assert.Equal(request.SampleCount, saved.SampleCount);
        Assert.Equal(request.SamplingMode, saved.SamplingMode);
        Assert.Equal(SimulationJobStatus.Pending, saved.Status);
        Assert.Equal(created.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Null(saved.ClaimedAtUtc);

        context.ChangeTracker.Clear();

        SimulationJobResult? retrieved =
            await repository.GetByIdAsync(
                created.Id,
                CancellationToken.None);

        Assert.Empty(
            context.ChangeTracker.Entries<SimulationJobEntity>());
        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal(request, retrieved.Request);
        Assert.Equal(created.Status, retrieved.Status);
        Assert.Equal(created.CreatedAtUtc, retrieved.CreatedAtUtc);
        Assert.Null(retrieved.ClaimedAtUtc);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNullWithoutModifyingData()
    {
        string connectionString = GetTestConnectionString();

        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        int jobCount = await context.SimulationJobs.CountAsync();
        int simulationCount = await context.Simulations.CountAsync();
        int stateCount = await context.SimulationStates.CountAsync();

        EfSimulationJobRepository repository = new(
            context);

        SimulationJobResult? result =
            await repository.GetByIdAsync(
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(jobCount, await context.SimulationJobs.CountAsync());
        Assert.Equal(
            simulationCount,
            await context.Simulations.CountAsync());
        Assert.Equal(
            stateCount,
            await context.SimulationStates.CountAsync());
        Assert.False(context.ChangeTracker.HasChanges());

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TryCompleteAsync_RunningJob_CompletesAndSimulationDeletionClearsReference()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);
        Guid jobId = Guid.NewGuid();
        Guid simulationId = Guid.NewGuid();
        await EnsureDatabaseMigratedAsync(options);
        await SeedSimulationAndJobsAsync(
            options,
            simulationId,
            CreateJob(
                jobId,
                DateTimeOffset.UtcNow,
                SimulationJobStatus.Running,
                DateTimeOffset.UtcNow));

        try
        {
            await using SingularFlowDbContext context = new(options);
            await context.Database.CloseConnectionAsync();
            Assert.Equal(
                ConnectionState.Closed,
                context.Database.GetDbConnection().State);

            EfSimulationJobRepository repository = new(context);
            DateTimeOffset timestampLowerBound =
                DateTimeOffset.UtcNow.AddSeconds(-5);

            SimulationJobResult? completed =
                await repository.TryCompleteAsync(
                    jobId,
                    simulationId,
                    CancellationToken.None);

            Assert.Equal(
                ConnectionState.Closed,
                context.Database.GetDbConnection().State);
            Assert.NotNull(completed);
            Assert.Equal(jobId, completed.Id);
            Assert.Equal(SimulationJobStatus.Completed, completed.Status);
            Assert.Equal(simulationId, completed.SimulationId);
            Assert.NotNull(completed.FinishedAtUtc);
            Assert.Equal(TimeSpan.Zero, completed.FinishedAtUtc.Value.Offset);
            Assert.InRange(
                completed.FinishedAtUtc.Value,
                timestampLowerBound,
                DateTimeOffset.UtcNow.AddSeconds(5));
            Assert.Null(completed.FailureMessage);
            Assert.Null(
                await repository.TryCompleteAsync(
                    jobId,
                    simulationId,
                    CancellationToken.None));
            Assert.Null(
                await repository.TryFailAsync(
                    jobId,
                    "A completed job cannot fail",
                    CancellationToken.None));
            Assert.Empty(
                context.ChangeTracker.Entries<SimulationJobEntity>());

            SimulationJobResult? retrieved =
                await repository.GetByIdAsync(
                    jobId,
                    CancellationToken.None);

            Assert.NotNull(retrieved);
            Assert.Equal(simulationId, retrieved.SimulationId);
            Assert.Equal(completed.FinishedAtUtc, retrieved.FinishedAtUtc);

            EfSimulationRepository simulationRepository = new(context);
            Assert.True(
                await simulationRepository.DeleteAsync(
                    simulationId,
                    CancellationToken.None));

            SimulationJobResult? retained =
                await repository.GetByIdAsync(
                    jobId,
                    CancellationToken.None);

            Assert.NotNull(retained);
            Assert.Equal(SimulationJobStatus.Completed, retained.Status);
            Assert.Null(retained.SimulationId);
            Assert.Equal(completed.FinishedAtUtc, retained.FinishedAtUtc);
        }
        finally
        {
            await DeleteExactJobsAsync(options, jobId);
            await DeleteExactSimulationAsync(options, simulationId);
        }
    }

    [Fact]
    public async Task TryCompleteAsync_MissingSimulation_PropagatesForeignKeyViolation()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);
        Guid jobId = Guid.NewGuid();
        await EnsureDatabaseMigratedAsync(options);
        await SeedJobsAsync(
            options,
            CreateJob(
                jobId,
                DateTimeOffset.UtcNow,
                SimulationJobStatus.Running,
                DateTimeOffset.UtcNow));

        try
        {
            await using SingularFlowDbContext context = new(options);
            EfSimulationJobRepository repository = new(context);

            PostgresException exception =
                await Assert.ThrowsAsync<PostgresException>(
                    () => repository.TryCompleteAsync(
                        jobId,
                        Guid.NewGuid(),
                        CancellationToken.None));

            Assert.Equal(
                PostgresErrorCodes.ForeignKeyViolation,
                exception.SqlState);
            Assert.Equal(
                ConnectionState.Closed,
                context.Database.GetDbConnection().State);

            SimulationJobEntity persisted =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == jobId);
            Assert.Equal(SimulationJobStatus.Running, persisted.Status);
            Assert.Null(persisted.SimulationId);
            Assert.Null(persisted.FinishedAtUtc);
        }
        finally
        {
            await DeleteExactJobsAsync(options, jobId);
        }
    }

    [Fact]
    public async Task TryFailAsync_OnlyRunningJobTransitionsAndTransactionCanRollback()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);
        Guid pendingId = Guid.NewGuid();
        Guid runningId = Guid.NewGuid();
        Guid completedId = Guid.NewGuid();
        Guid failedId = Guid.NewGuid();
        Guid[] seededIds = [pendingId, runningId, completedId, failedId];
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        await EnsureDatabaseMigratedAsync(options);
        await SeedJobsAsync(
            options,
            CreateJob(
                pendingId,
                timestamp,
                SimulationJobStatus.Pending,
                null),
            CreateJob(
                runningId,
                timestamp,
                SimulationJobStatus.Running,
                timestamp),
            CreateJob(
                completedId,
                timestamp,
                SimulationJobStatus.Completed,
                timestamp,
                finishedAtUtc: timestamp.AddSeconds(1)),
            CreateJob(
                failedId,
                timestamp,
                SimulationJobStatus.Failed,
                timestamp,
                finishedAtUtc: timestamp.AddSeconds(1),
                failureMessage: "Original failure"));

        try
        {
            await using SingularFlowDbContext context = new(options);
            await using var transaction =
                await context.Database.BeginTransactionAsync();
            Assert.Equal(
                ConnectionState.Open,
                context.Database.GetDbConnection().State);
            EfSimulationJobRepository repository = new(context);

            SimulationJobResult? failed =
                await repository.TryFailAsync(
                    runningId,
                    "Bounded failure description",
                    CancellationToken.None);

            Assert.NotNull(failed);
            Assert.Equal(SimulationJobStatus.Failed, failed.Status);
            Assert.Null(failed.SimulationId);
            Assert.NotNull(failed.FinishedAtUtc);
            Assert.Equal(
                "Bounded failure description",
                failed.FailureMessage);
            Assert.Equal(
                ConnectionState.Open,
                context.Database.GetDbConnection().State);

            Assert.Null(
                await repository.TryCompleteAsync(
                    runningId,
                    Guid.NewGuid(),
                    CancellationToken.None));
            Assert.Null(
                await repository.TryFailAsync(
                    runningId,
                    "Conflicting failure",
                    CancellationToken.None));
            Assert.Null(
                await repository.TryCompleteAsync(
                    pendingId,
                    Guid.NewGuid(),
                    CancellationToken.None));
            Assert.Null(
                await repository.TryFailAsync(
                    pendingId,
                    "Pending cannot fail",
                    CancellationToken.None));
            Assert.Null(
                await repository.TryFailAsync(
                    completedId,
                    "Completed cannot fail",
                    CancellationToken.None));
            Assert.Null(
                await repository.TryCompleteAsync(
                    failedId,
                    Guid.NewGuid(),
                    CancellationToken.None));

            await transaction.RollbackAsync();

            SimulationJobEntity persisted =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == runningId);
            Assert.Equal(SimulationJobStatus.Running, persisted.Status);
            Assert.Null(persisted.FinishedAtUtc);
            Assert.Null(persisted.FailureMessage);
        }
        finally
        {
            await DeleteExactJobsAsync(options, seededIds);
        }
    }

    [Fact]
    public async Task TryCompleteAndTryFailAsync_ConcurrentTerminalTransitions_HaveOneWinner()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);
        Guid jobId = Guid.NewGuid();
        Guid simulationId = Guid.NewGuid();
        await EnsureDatabaseMigratedAsync(options);
        await SeedSimulationAndJobsAsync(
            options,
            simulationId,
            CreateJob(
                jobId,
                DateTimeOffset.UtcNow,
                SimulationJobStatus.Running,
                DateTimeOffset.UtcNow));

        try
        {
            await using SingularFlowDbContext completionContext =
                new(options);
            await using SingularFlowDbContext failureContext =
                new(options);
            EfSimulationJobRepository completionRepository = new(
                completionContext);
            EfSimulationJobRepository failureRepository = new(
                failureContext);
            TaskCompletionSource startGate = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource completionReady = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource failureReady = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource timeout =
                new(TimeSpan.FromSeconds(10));

            Task<SimulationJobResult?> completion = RunTransitionWhenReleasedAsync(
                () => completionRepository.TryCompleteAsync(
                    jobId,
                    simulationId,
                    timeout.Token),
                completionReady,
                startGate.Task);
            Task<SimulationJobResult?> failure = RunTransitionWhenReleasedAsync(
                () => failureRepository.TryFailAsync(
                    jobId,
                    "Concurrent failure",
                    timeout.Token),
                failureReady,
                startGate.Task);

            await Task.WhenAll(completionReady.Task, failureReady.Task);
            startGate.SetResult();

            SimulationJobResult?[] results =
                await Task.WhenAll(completion, failure);
            Assert.False(timeout.IsCancellationRequested);
            SimulationJobResult winner = Assert.Single(
                results.OfType<SimulationJobResult>());
            Assert.True(
                winner.Status is
                    SimulationJobStatus.Completed or
                    SimulationJobStatus.Failed);

            await using SingularFlowDbContext verificationContext =
                new(options);
            SimulationJobEntity persisted =
                await verificationContext.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == jobId);
            Assert.Equal(winner.Status, persisted.Status);
            Assert.NotNull(persisted.FinishedAtUtc);
            Assert.Equal(
                winner.SimulationId,
                persisted.SimulationId);
            Assert.Equal(
                winner.FailureMessage,
                persisted.FailureMessage);
        }
        finally
        {
            await DeleteExactJobsAsync(options, jobId);
            await DeleteExactSimulationAsync(options, simulationId);
        }
    }

    [Fact]
    public async Task ClaimNextPendingAsync_NoPendingJobs_ReturnsNullAndRestoresConnectionState()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
        await context.Database.CloseConnectionAsync();

        int pendingCount = await context.SimulationJobs.CountAsync(
            job => job.Status == SimulationJobStatus.Pending);
        Assert.True(
            pendingCount == 0,
            "Protected test database contains an unrelated Pending " +
            "simulation job.");

        int jobCount = await context.SimulationJobs.CountAsync();
        int simulationCount = await context.Simulations.CountAsync();
        int stateCount = await context.SimulationStates.CountAsync();
        await context.Database.CloseConnectionAsync();
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection().State);

        EfSimulationJobRepository repository = new(context);

        SimulationJobResult? result =
            await repository.ClaimNextPendingAsync(
                CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection().State);
        Assert.Empty(
            context.ChangeTracker.Entries<SimulationJobEntity>());
        Assert.Equal(jobCount, await context.SimulationJobs.CountAsync());
        Assert.Equal(
            simulationCount,
            await context.Simulations.CountAsync());
        Assert.Equal(
            stateCount,
            await context.SimulationStates.CountAsync());
    }

    [Fact]
    public async Task ClaimNextPendingAsync_OnePendingJob_TransitionsAndReturnsCompleteResult()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();

        DateTimeOffset timestampLowerBound = DateTimeOffset.UtcNow;
        await using var transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            int simulationCount = await context.Simulations.CountAsync();
            int stateCount = await context.SimulationStates.CountAsync();
            RunSimulationRequest request = new(
                SingularTime: 2.5,
                ConcentrationExponent: 0.004,
                StartTime: 0.25,
                EndTime: 1.75,
                SampleCount: 17,
                SamplingMode: SamplingMode.Logarithmic);
            EfSimulationJobRepository repository = new(context);

            SimulationJobResult created =
                await repository.CreateAsync(
                    request,
                    CancellationToken.None);

            context.ChangeTracker.Clear();

            SimulationJobResult? claimed =
                await repository.ClaimNextPendingAsync(
                    CancellationToken.None);

            Assert.Equal(
                ConnectionState.Open,
                context.Database.GetDbConnection().State);
            Assert.NotNull(claimed);
            Assert.Equal(created.Id, claimed.Id);
            Assert.Equal(request, claimed.Request);
            Assert.Equal(created.CreatedAtUtc, claimed.CreatedAtUtc);
            Assert.Equal(SimulationJobStatus.Running, claimed.Status);
            Assert.NotNull(claimed.ClaimedAtUtc);
            Assert.Equal(TimeSpan.Zero, claimed.ClaimedAtUtc.Value.Offset);
            Assert.InRange(
                claimed.ClaimedAtUtc.Value,
                timestampLowerBound.AddSeconds(-5),
                DateTimeOffset.UtcNow.AddSeconds(5));
            Assert.Empty(
                context.ChangeTracker.Entries<SimulationJobEntity>());

            SimulationJobEntity persisted =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == created.Id);

            Assert.Equal(SimulationJobStatus.Running, persisted.Status);
            Assert.Equal(claimed.ClaimedAtUtc, persisted.ClaimedAtUtc);
            Assert.Equal(
                simulationCount,
                await context.Simulations.CountAsync());
            Assert.Equal(
                stateCount,
                await context.SimulationStates.CountAsync());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ClaimNextPendingAsync_SelectsOldestPendingAndIgnoresOtherStatuses()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
        await using var transaction =
            await context.Database.BeginTransactionAsync();

        DateTimeOffset oldest = new(
            2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = oldest.AddMinutes(1);
        DateTimeOffset claimedAt = oldest.AddMinutes(-1);
        Guid oldestPendingId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");
        Guid laterPendingId = Guid.Parse(
            "10000000-0000-0000-0000-000000000002");
        SimulationJobEntity[] nonPendingJobs =
        [
            CreateJob(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                oldest.AddMinutes(-4),
                SimulationJobStatus.Running,
                claimedAt),
            CreateJob(
                Guid.Parse("20000000-0000-0000-0000-000000000002"),
                oldest.AddMinutes(-3),
                SimulationJobStatus.Completed,
                claimedAt,
                finishedAtUtc: claimedAt.AddSeconds(1)),
            CreateJob(
                Guid.Parse("20000000-0000-0000-0000-000000000003"),
                oldest.AddMinutes(-2),
                SimulationJobStatus.Failed,
                claimedAt,
                finishedAtUtc: claimedAt.AddSeconds(1),
                failureMessage: "Seeded failure"),
            CreateJob(
                Guid.Parse("20000000-0000-0000-0000-000000000004"),
                oldest.AddMinutes(-1),
                SimulationJobStatus.Cancelled,
                claimedAt)
        ];

        try
        {
            context.SimulationJobs.AddRange(nonPendingJobs);
            context.SimulationJobs.AddRange(
                CreateJob(
                    oldestPendingId,
                    oldest,
                    SimulationJobStatus.Pending,
                    null),
                CreateJob(
                    laterPendingId,
                    later,
                    SimulationJobStatus.Pending,
                    null));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            EfSimulationJobRepository repository = new(context);
            SimulationJobResult? claimed =
                await repository.ClaimNextPendingAsync(
                    CancellationToken.None);

            Assert.NotNull(claimed);
            Assert.Equal(oldestPendingId, claimed.Id);

            SimulationJobEntity laterPending =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == laterPendingId);
            Assert.Equal(SimulationJobStatus.Pending, laterPending.Status);
            Assert.Null(laterPending.ClaimedAtUtc);

            foreach (SimulationJobEntity expected in nonPendingJobs)
            {
                SimulationJobEntity persisted =
                    await context.SimulationJobs
                        .AsNoTracking()
                        .SingleAsync(job => job.Id == expected.Id);

                Assert.Equal(expected.Status, persisted.Status);
                Assert.Equal(expected.ClaimedAtUtc, persisted.ClaimedAtUtc);
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ClaimNextPendingAsync_EqualCreatedAt_UsesAscendingId()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
        await using var transaction =
            await context.Database.BeginTransactionAsync();

        DateTimeOffset createdAt = new(
            2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Guid lowerId = Guid.Parse(
            "00000000-0000-0000-0000-000000000001");
        Guid higherId = Guid.Parse(
            "00000000-0000-0000-0000-000000000002");

        try
        {
            context.SimulationJobs.AddRange(
                CreateJob(
                    higherId,
                    createdAt,
                    SimulationJobStatus.Pending,
                    null),
                CreateJob(
                    lowerId,
                    createdAt,
                    SimulationJobStatus.Pending,
                    null));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            EfSimulationJobRepository repository = new(context);
            SimulationJobResult? claimed =
                await repository.ClaimNextPendingAsync(
                    CancellationToken.None);

            Assert.NotNull(claimed);
            // PostgreSQL compares UUIDs by their unsigned 128-bit value;
            // these IDs differ only in the final byte (01 before 02).
            Assert.Equal(lowerId, claimed.Id);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ClaimNextPendingAsync_LockedOnlyJob_IsSkippedWithoutBlocking()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);
        Guid jobId = Guid.Parse(
            "30000000-0000-0000-0000-000000000001");

        await AssertNoPendingJobsAsync(options);
        await SeedJobsAsync(
            options,
            CreateJob(
                jobId,
                DateTimeOffset.UtcNow,
                SimulationJobStatus.Pending,
                null));

        try
        {
            await using SingularFlowDbContext contextA = new(options);
            await using SingularFlowDbContext contextB = new(options);
            await contextA.Database.OpenConnectionAsync();
            await contextB.Database.OpenConnectionAsync();
            await using var transactionA =
                await contextA.Database.BeginTransactionAsync();

            try
            {
                EfSimulationJobRepository repositoryA = new(contextA);
                EfSimulationJobRepository repositoryB = new(contextB);

                SimulationJobResult? claimedByA =
                    await repositoryA.ClaimNextPendingAsync(
                        CancellationToken.None);
                Assert.NotNull(claimedByA);
                Assert.Equal(jobId, claimedByA.Id);

                using CancellationTokenSource timeout =
                    new(TimeSpan.FromSeconds(5));
                SimulationJobResult? claimedByB =
                    await repositoryB.ClaimNextPendingAsync(
                        timeout.Token);

                Assert.False(timeout.IsCancellationRequested);
                Assert.Null(claimedByB);
            }
            finally
            {
                await transactionA.RollbackAsync();
            }
        }
        finally
        {
            await DeleteExactJobsAsync(options, jobId);
        }
    }

    [Fact]
    public async Task ClaimNextPendingAsync_TwoPendingJobs_ConcurrentClaimersReturnDistinctJobs()
    {
        string connectionString = GetTestConnectionString();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);
        Guid firstId = Guid.Parse(
            "40000000-0000-0000-0000-000000000001");
        Guid secondId = Guid.Parse(
            "40000000-0000-0000-0000-000000000002");
        Guid[] seededIds = [firstId, secondId];

        await AssertNoPendingJobsAsync(options);
        await SeedJobsAsync(
            options,
            CreateJob(
                firstId,
                DateTimeOffset.UtcNow,
                SimulationJobStatus.Pending,
                null),
            CreateJob(
                secondId,
                DateTimeOffset.UtcNow.AddMinutes(1),
                SimulationJobStatus.Pending,
                null));

        try
        {
            await using SingularFlowDbContext firstContext = new(options);
            await using SingularFlowDbContext secondContext = new(options);
            await firstContext.Database.OpenConnectionAsync();
            await secondContext.Database.OpenConnectionAsync();

            EfSimulationJobRepository firstRepository = new(firstContext);
            EfSimulationJobRepository secondRepository = new(secondContext);
            TaskCompletionSource startGate = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource firstReady = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondReady = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task<SimulationJobResult?> firstClaim = ClaimWhenReleasedAsync(
                firstRepository,
                firstReady,
                startGate.Task);
            Task<SimulationJobResult?> secondClaim = ClaimWhenReleasedAsync(
                secondRepository,
                secondReady,
                startGate.Task);

            await Task.WhenAll(firstReady.Task, secondReady.Task);
            startGate.SetResult();

            SimulationJobResult?[] results =
                await Task.WhenAll(firstClaim, secondClaim);

            Assert.All(results, Assert.NotNull);
            Guid[] claimedIds = results
                .Select(result => result!.Id)
                .ToArray();
            Assert.Equal(2, claimedIds.Distinct().Count());
            Assert.All(
                claimedIds,
                id => Assert.Contains(id, seededIds));
            Assert.All(
                results,
                result =>
                {
                    Assert.Equal(
                        SimulationJobStatus.Running,
                        result!.Status);
                    Assert.NotNull(result.ClaimedAtUtc);
                });

            await using SingularFlowDbContext verificationContext =
                new(options);
            SimulationJobEntity[] persisted =
                await verificationContext.SimulationJobs
                    .AsNoTracking()
                    .Where(job => seededIds.Contains(job.Id))
                    .ToArrayAsync();

            Assert.Equal(2, persisted.Length);
            Assert.All(
                persisted,
                job =>
                {
                    Assert.Equal(SimulationJobStatus.Running, job.Status);
                    Assert.NotNull(job.ClaimedAtUtc);
                });
        }
        finally
        {
            await DeleteExactJobsAsync(options, seededIds);
        }
    }

    private static async Task<SimulationJobResult?> ClaimWhenReleasedAsync(
        EfSimulationJobRepository repository,
        TaskCompletionSource ready,
        Task startGate)
    {
        ready.SetResult();
        await startGate;

        return await repository.ClaimNextPendingAsync(
            CancellationToken.None);
    }

    private static async Task<SimulationJobResult?> RunTransitionWhenReleasedAsync(
        Func<Task<SimulationJobResult?>> transition,
        TaskCompletionSource ready,
        Task startGate)
    {
        ready.SetResult();
        await startGate;

        return await transition();
    }

    private static SimulationJobEntity CreateJob(
        Guid id,
        DateTimeOffset createdAtUtc,
        SimulationJobStatus status,
        DateTimeOffset? claimedAtUtc,
        DateTimeOffset? finishedAtUtc = null,
        Guid? simulationId = null,
        string? failureMessage = null)
    {
        return new SimulationJobEntity
        {
            Id = id,
            SingularTime = 2.5,
            ConcentrationExponent = 0.004,
            StartTime = 0.25,
            EndTime = 1.75,
            SampleCount = 17,
            SamplingMode = SamplingMode.Logarithmic,
            Status = status,
            CreatedAtUtc = createdAtUtc,
            ClaimedAtUtc = claimedAtUtc,
            FinishedAtUtc = finishedAtUtc,
            SimulationId = simulationId,
            FailureMessage = failureMessage
        };
    }

    private static async Task SeedJobsAsync(
        DbContextOptions<SingularFlowDbContext> options,
        params SimulationJobEntity[] jobs)
    {
        await using SingularFlowDbContext context = new(options);
        context.SimulationJobs.AddRange(jobs);
        await context.SaveChangesAsync();
    }

    private static async Task EnsureDatabaseMigratedAsync(
        DbContextOptions<SingularFlowDbContext> options)
    {
        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
    }

    private static async Task SeedSimulationAndJobsAsync(
        DbContextOptions<SingularFlowDbContext> options,
        Guid simulationId,
        params SimulationJobEntity[] jobs)
    {
        await using SingularFlowDbContext context = new(options);
        context.Simulations.Add(
            new SimulationEntity
            {
                Id = simulationId,
                SingularTime = 2.5,
                ConcentrationExponent = 0.004,
                StartTime = 0.25,
                EndTime = 1.75,
                SampleCount = 17,
                SamplingMode = SamplingMode.Logarithmic
            });
        context.SimulationJobs.AddRange(jobs);
        await context.SaveChangesAsync();
    }

    private static async Task AssertNoPendingJobsAsync(
        DbContextOptions<SingularFlowDbContext> options)
    {
        await using SingularFlowDbContext context = new(options);
        int pendingCount = await context.SimulationJobs.CountAsync(
            job => job.Status == SimulationJobStatus.Pending);

        Assert.True(
            pendingCount == 0,
            "Protected test database contains an unrelated Pending " +
            "simulation job.");
    }

    private static async Task DeleteExactJobsAsync(
        DbContextOptions<SingularFlowDbContext> options,
        params Guid[] ids)
    {
        await using SingularFlowDbContext context = new(options);
        await context.SimulationJobs
            .Where(job => ids.Contains(job.Id))
            .ExecuteDeleteAsync();

        Assert.False(
            await context.SimulationJobs.AnyAsync(
                job => ids.Contains(job.Id)));
    }

    private static async Task DeleteExactSimulationAsync(
        DbContextOptions<SingularFlowDbContext> options,
        Guid id)
    {
        await using SingularFlowDbContext context = new(options);
        await context.Simulations
            .Where(simulation => simulation.Id == id)
            .ExecuteDeleteAsync();

        Assert.False(
            await context.Simulations.AnyAsync(
                simulation => simulation.Id == id));
    }

    private static string GetTestConnectionString()
    {
        string connectionString =
            Environment.GetEnvironmentVariable(
                "SINGULARFLOW_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");

        NpgsqlConnectionStringBuilder connection = new(
            connectionString);

        Assert.Equal(
            "singular_flow_tests",
            connection.Database);

        return connectionString;
    }

    private static DbContextOptions<SingularFlowDbContext> CreateOptions(
        string connectionString)
    {
        return new DbContextOptionsBuilder<SingularFlowDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }
}