using System.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Domain.Models;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

[CollectionDefinition(
    "Simulation job completion database tests",
    DisableParallelization = true)]
public sealed class SimulationJobCompletionDatabaseCollection;

[Collection("Simulation job completion database tests")]
public sealed class EfSimulationJobCompletionRepositoryTests
{
    private static readonly RunSimulationRequest Request = new(
        SingularTime: 2.75,
        ConcentrationExponent: 0.003,
        StartTime: 0.125,
        EndTime: 0.875,
        SampleCount: 3,
        SamplingMode: SamplingMode.Logarithmic);

    private static readonly RunSimulationResult Simulation =
        CreateSimulation();

    private static readonly Guid ClaimToken =
        Guid.Parse("c24564dd-e33e-4ea8-98c9-9f2a65a0265d");

    [Fact]
    public async Task SaveSimulationAndCompleteAsync_ValidRunningJob_CommitsSimulationStatesAndCompletion()
    {
        SimulationIdCaptureInterceptor capture = new();
        DbContextOptions<SingularFlowDbContext> options =
            await CreateProtectedOptionsAsync(capture);
        await EnsureDatabaseMigratedAsync(options);
        Guid jobId = Guid.Empty;
        Guid? simulationId = null;

        try
        {
            jobId = await CreateRunningJobAsync(options);

            await using SingularFlowDbContext context = new(options);
            EfSimulationJobCompletionRepository repository = new(context);

            SimulationJobResult? completed =
                await repository.SaveSimulationAndCompleteAsync(
                    jobId,
                    ClaimToken,
                    Simulation,
                    CancellationToken.None);

            Assert.NotNull(completed);
            Assert.Equal(jobId, completed.Id);
            Assert.Equal(SimulationJobStatus.Completed, completed.Status);
            Assert.NotNull(completed.SimulationId);
            Assert.NotEqual(Guid.Empty, completed.SimulationId.Value);
            Assert.NotNull(completed.FinishedAtUtc);
            Assert.Null(completed.FailureMessage);
            simulationId = completed.SimulationId;
            Assert.Equal(simulationId, capture.SimulationId);

            await using SingularFlowDbContext verification = new(options);
            SimulationJobEntity persistedJob =
                await verification.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == jobId);
            Assert.Equal(completed.SimulationId, persistedJob.SimulationId);
            Assert.Equal(completed.Status, persistedJob.Status);
            Assert.Equal(completed.FinishedAtUtc, persistedJob.FinishedAtUtc);
            Assert.Null(persistedJob.FailureMessage);
            Assert.Null(persistedJob.ClaimToken);
            Assert.Null(persistedJob.LeaseExpiresAtUtc);

            SimulationEntity? persistedSimulation =
                await verification.Simulations
                    .AsNoTracking()
                    .Include(entity => entity.States)
                    .SingleOrDefaultAsync(entity => entity.Id == simulationId);
            Assert.NotNull(persistedSimulation);
            Assert.Equal(
                Simulation.BlowupParameters.SingularTime,
                persistedSimulation.SingularTime);
            Assert.Equal(
                Simulation.BlowupParameters.ConcentrationExponent,
                persistedSimulation.ConcentrationExponent);
            Assert.Equal(
                Simulation.TimeSeriesParameters.StartTime,
                persistedSimulation.StartTime);
            Assert.Equal(
                Simulation.TimeSeriesParameters.EndTime,
                persistedSimulation.EndTime);
            Assert.Equal(
                Simulation.TimeSeriesParameters.SampleCount,
                persistedSimulation.SampleCount);
            Assert.Equal(Simulation.SamplingMode, persistedSimulation.SamplingMode);

            SimulationStateEntity[] states =
                await verification.SimulationStates
                    .AsNoTracking()
                    .Where(state => state.SimulationId == simulationId)
                    .OrderBy(state => state.Sequence)
                    .ToArrayAsync();
            Assert.Equal(Simulation.States.Count, states.Length);

            for (int index = 0; index < states.Length; index++)
            {
                Assert.Equal(index, states[index].Sequence);
                Assert.Equal(
                    Simulation.States[index],
                    new BlowupState(
                        states[index].Time,
                        states[index].RemainingTime,
                        states[index].RadialLength,
                        states[index].AxialLength,
                        states[index].AngularVelocityScale,
                        states[index].RadialVelocityScale,
                        states[index].CoreVolumeScale,
                        states[index].CoreEnergyScale));
            }

            EfSimulationRepository simulationRepository = new(verification);
            PersistedSimulationResult? reconstructed =
                await simulationRepository.GetByIdAsync(
                    simulationId.Value,
                    CancellationToken.None);
            Assert.NotNull(reconstructed);
            Assert.Equal(
                Simulation.BlowupParameters,
                reconstructed.Simulation.BlowupParameters);
            Assert.Equal(
                Simulation.TimeSeriesParameters,
                reconstructed.Simulation.TimeSeriesParameters);
            Assert.Equal(
                Simulation.SamplingMode,
                reconstructed.Simulation.SamplingMode);
            Assert.Equal(
                Simulation.States,
                reconstructed.Simulation.States);

        }
        finally
        {
            if (jobId != Guid.Empty)
            {
                await DeleteExactJobsAsync(options, jobId);
            }

            if (simulationId is Guid id)
            {
                await DeleteExactSimulationAsync(options, id);
            }
        }
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Pending")]
    [InlineData("Completed")]
    [InlineData("Failed")]
    public async Task SaveSimulationAndCompleteAsync_NonRunningJob_RollsBackAndReturnsNull(
        string jobState)
    {
        SimulationIdCaptureInterceptor capture = new();
        DbContextOptions<SingularFlowDbContext> options =
            await CreateProtectedOptionsAsync(capture);
        await EnsureDatabaseMigratedAsync(options);
        Guid jobId = Guid.NewGuid();
        Guid? simulationId = null;
        bool seededJob = jobState != "Missing";

        try
        {
            if (seededJob)
            {
                await SeedJobAsync(
                    options,
                    CreateJob(jobId, GetStatus(jobState)));
            }

            await using SingularFlowDbContext context = new(options);
            EfSimulationJobCompletionRepository repository = new(context);

            SimulationJobResult? result =
                await repository.SaveSimulationAndCompleteAsync(
                    jobId,
                    ClaimToken,
                    Simulation,
                    CancellationToken.None);

            Assert.Null(result);
            simulationId = capture.SimulationId;
            Assert.NotNull(simulationId);
            Assert.DoesNotContain(
                context.ChangeTracker.Entries<SimulationEntity>(),
                entry => entry.State != EntityState.Detached);
            Assert.DoesNotContain(
                context.ChangeTracker.Entries<SimulationStateEntity>(),
                entry => entry.State != EntityState.Detached);

            if (seededJob)
            {
                SimulationJobEntity unchanged =
                    await context.SimulationJobs
                        .AsNoTracking()
                        .SingleAsync(job => job.Id == jobId);
                Assert.Equal(GetStatus(jobState), unchanged.Status);
                Assert.Null(unchanged.SimulationId);
                Assert.Equal(
                    jobState == "Pending" ? null : GetTimestamp(),
                    unchanged.FinishedAtUtc);
                Assert.Equal(
                    jobState == "Failed" ? "Seeded failure." : null,
                    unchanged.FailureMessage);
            }
            else
            {
                Assert.False(
                    await context.SimulationJobs
                        .AnyAsync(job => job.Id == jobId));
            }

            Assert.False(
                await context.Simulations
                    .AnyAsync(simulation => simulation.Id == simulationId));
            Assert.False(
                await context.SimulationStates
                    .AnyAsync(state => state.SimulationId == simulationId));
        }
        finally
        {
            if (seededJob)
            {
                await DeleteExactJobsAsync(options, jobId);
            }

            if (simulationId is Guid id)
            {
                await DeleteExactSimulationAsync(options, id);
            }
        }
    }

    [Fact]
    public async Task SaveSimulationAndCompleteAsync_SimulationPersistenceFails_RollsBackAndPreservesRunningJob()
    {
        InvalidSimulationStateSequenceInterceptor interceptor = new();
        DbContextOptions<SingularFlowDbContext> options =
            await CreateProtectedOptionsAsync(interceptor);
        await EnsureDatabaseMigratedAsync(options);
        Guid jobId = Guid.Empty;
        Guid? simulationId = null;

        try
        {
            jobId = await CreateRunningJobAsync(options);

            await using SingularFlowDbContext context = new(options);
            EfSimulationJobCompletionRepository repository = new(context);

            DbUpdateException exception =
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => repository.SaveSimulationAndCompleteAsync(
                        jobId,
                        ClaimToken,
                        Simulation,
                        CancellationToken.None));

            PostgresException postgresException =
                Assert.IsType<PostgresException>(
                    exception.GetBaseException());
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                postgresException.SqlState);
            Assert.Equal(
                "ck_simulation_states_sequence",
                postgresException.ConstraintName);
            simulationId = interceptor.SimulationId;
            Assert.NotNull(simulationId);
            Assert.DoesNotContain(
                context.ChangeTracker.Entries<SimulationEntity>(),
                entry => entry.State != EntityState.Detached);
            Assert.DoesNotContain(
                context.ChangeTracker.Entries<SimulationStateEntity>(),
                entry => entry.State != EntityState.Detached);

            SimulationJobEntity job =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == jobId);
            Assert.Equal(SimulationJobStatus.Running, job.Status);
            Assert.Null(job.SimulationId);
            Assert.Null(job.FinishedAtUtc);
            Assert.Null(job.FailureMessage);
            Assert.Equal(ClaimToken, job.ClaimToken);
            Assert.NotNull(job.LeaseExpiresAtUtc);
            Assert.False(
                await context.Simulations
                    .AnyAsync(entity => entity.Id == simulationId));
            Assert.False(
                await context.SimulationStates
                    .AnyAsync(entity => entity.SimulationId == simulationId));

            int followUpChanges = await context.SaveChangesAsync();
            Assert.Equal(0, followUpChanges);

            Assert.False(
                await context.Simulations
                    .AnyAsync(entity => entity.Id == simulationId));
            Assert.False(
                await context.SimulationStates
                    .AnyAsync(state => state.SimulationId == simulationId));

            SimulationJobEntity jobAfterFollowUpSave =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == jobId);
            Assert.Equal(
                SimulationJobStatus.Running,
                jobAfterFollowUpSave.Status);
            Assert.Null(jobAfterFollowUpSave.SimulationId);
            Assert.Null(jobAfterFollowUpSave.FinishedAtUtc);
            Assert.Null(jobAfterFollowUpSave.FailureMessage);
            Assert.Equal(ClaimToken, jobAfterFollowUpSave.ClaimToken);
            Assert.NotNull(jobAfterFollowUpSave.LeaseExpiresAtUtc);
        }
        finally
        {
            if (jobId != Guid.Empty)
            {
                await DeleteExactJobsAsync(options, jobId);
            }

            if (simulationId is Guid id)
            {
                await DeleteExactSimulationAsync(options, id);
            }
        }
    }

    [Fact]
    public async Task SaveSimulationAndCompleteAsync_PreCancelledToken_WritesNothing()
    {
        DbContextOptions<SingularFlowDbContext> options =
            await CreateProtectedOptionsAsync();
        await EnsureDatabaseMigratedAsync(options);
        Guid jobId = await CreateRunningJobAsync(options);
        SimulationIdCaptureInterceptor capture = new();
        options = await CreateProtectedOptionsAsync(capture);

        try
        {
            await using SingularFlowDbContext context = new(options);
            EfSimulationJobCompletionRepository repository = new(context);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => repository.SaveSimulationAndCompleteAsync(
                    jobId,
                    ClaimToken,
                    Simulation,
                    cancellation.Token));

            SimulationJobEntity job =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == jobId);
            Assert.Equal(SimulationJobStatus.Running, job.Status);
            Assert.Null(job.SimulationId);
            Assert.Null(job.FinishedAtUtc);
            Assert.Null(job.FailureMessage);
            Assert.Equal(ClaimToken, job.ClaimToken);
            Assert.NotNull(job.LeaseExpiresAtUtc);
            Assert.Empty(context.ChangeTracker.Entries<SimulationEntity>());
            Assert.Empty(context.ChangeTracker.Entries<SimulationStateEntity>());
            Assert.Null(capture.SimulationId);
            Assert.Equal(0, capture.SimulationSaveInvocationCount);
        }
        finally
        {
            await DeleteExactJobsAsync(options, jobId);
        }
    }

    [Fact]
    public async Task SaveSimulationAndCompleteAsync_InsideExistingTransaction_UsesSavepointAndPreservesCallerTransaction()
    {
        SimulationIdCaptureInterceptor capture = new();
        DbContextOptions<SingularFlowDbContext> seedOptions =
            await CreateProtectedOptionsAsync();
        await EnsureDatabaseMigratedAsync(seedOptions);
        DbContextOptions<SingularFlowDbContext> options =
            await CreateProtectedOptionsAsync(capture);
        Guid unrelatedSimulationId = Guid.NewGuid();
        Guid missingJobId = Guid.NewGuid();
        Guid? attemptedSimulationId = null;

        try
        {
            await using SingularFlowDbContext context = new(options);
            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync();
            SimulationEntity callerEntity = new()
            {
                Id = unrelatedSimulationId,
                SingularTime = 4.25,
                ConcentrationExponent = 0.003,
                StartTime = 0.125,
                EndTime = 0.875,
                SampleCount = 3,
                SamplingMode = SamplingMode.Uniform
            };
            context.Simulations.Add(callerEntity);
            await context.SaveChangesAsync();
            EntityState callerState = context.Entry(callerEntity).State;
            Assert.Equal(EntityState.Unchanged, callerState);
            Assert.Equal(unrelatedSimulationId, capture.SimulationId);
            IDbContextTransaction callerTransaction =
                context.Database.CurrentTransaction!;
            EfSimulationJobCompletionRepository repository = new(context);

            SimulationJobResult? result =
                await repository.SaveSimulationAndCompleteAsync(
                    missingJobId,
                    ClaimToken,
                    Simulation,
                    CancellationToken.None);

            Assert.Null(result);
            attemptedSimulationId = capture.SimulationId;
            Assert.NotNull(attemptedSimulationId);
            Assert.NotEqual(unrelatedSimulationId, attemptedSimulationId);
            Assert.Equal(2, capture.SimulationSaveInvocationCount);
            Assert.Same(
                callerTransaction,
                context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, context.Database
                .GetDbConnection().State);
            Assert.Same(
                callerEntity,
                context.ChangeTracker
                    .Entries<SimulationEntity>()
                    .Single(entry => entry.Entity.Id == unrelatedSimulationId)
                    .Entity);
            Assert.Equal(callerState, context.Entry(callerEntity).State);
            Assert.False(
                await context.Simulations.AnyAsync(
                    entity => entity.Id == attemptedSimulationId));
            Assert.DoesNotContain(
                context.ChangeTracker.Entries<SimulationStateEntity>(),
                entry => entry.State != EntityState.Detached);
            Assert.True(
                await context.Simulations.AnyAsync(
                    entity => entity.Id == unrelatedSimulationId));
            Assert.False(
                await context.SimulationJobs.AnyAsync(
                    entity => entity.Id == missingJobId));
            Assert.False(
                await context.SimulationStates.AnyAsync(
                    state => state.SimulationId == attemptedSimulationId));

            await using (SingularFlowDbContext outsideTransaction =
                new(seedOptions))
            {
                Assert.False(
                    await outsideTransaction.Simulations.AnyAsync(
                        entity => entity.Id == unrelatedSimulationId));
                Assert.False(
                    await outsideTransaction.Simulations.AnyAsync(
                        entity => entity.Id == attemptedSimulationId));
                Assert.False(
                    await outsideTransaction.SimulationStates.AnyAsync(
                        state => state.SimulationId == attemptedSimulationId));
            }

            await transaction.CreateSavepointAsync("caller_transaction_usable");
            await transaction.RollbackToSavepointAsync(
                "caller_transaction_usable");
            await transaction.ReleaseSavepointAsync(
                "caller_transaction_usable");
            Assert.Same(callerTransaction, context.Database.CurrentTransaction);
            Assert.True(
                await context.Simulations.AnyAsync(
                    entity => entity.Id == unrelatedSimulationId));
            await transaction.RollbackAsync();

            await using SingularFlowDbContext afterCallerRollback =
                new(seedOptions);
            Assert.False(
                await afterCallerRollback.Simulations.AnyAsync(
                    entity => entity.Id == unrelatedSimulationId));
            Assert.False(
                await afterCallerRollback.Simulations.AnyAsync(
                    entity => entity.Id == attemptedSimulationId));
        }
        finally
        {
            if (attemptedSimulationId is Guid attemptedId)
            {
                await DeleteExactSimulationAsync(seedOptions, attemptedId);
            }

            await DeleteExactSimulationAsync(
                seedOptions,
                unrelatedSimulationId);
        }
    }

    [Fact]
    public async Task SaveSimulationAndCompleteAsync_WithPendingCallerChanges_RejectsWithoutFlushingOrRollingBackThem()
    {
        DbContextOptions<SingularFlowDbContext> seedOptions =
            await CreateProtectedOptionsAsync();
        await EnsureDatabaseMigratedAsync(seedOptions);
        Guid jobId = await CreateRunningJobAsync(seedOptions);
        Guid unrelatedSimulationId = Guid.NewGuid();
        SimulationIdCaptureInterceptor capture = new();
        DbContextOptions<SingularFlowDbContext> observingOptions =
            await CreateProtectedOptionsAsync(capture);

        try
        {
            await SeedSimulationAsync(seedOptions, unrelatedSimulationId);

            await using SingularFlowDbContext context =
                new(observingOptions);
            SimulationEntity callerEntity =
                await context.Simulations
                    .SingleAsync(entity => entity.Id == unrelatedSimulationId);
            callerEntity.SampleCount = 4;
            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync();
            IDbContextTransaction callerTransaction =
                context.Database.CurrentTransaction!;
            EfSimulationJobCompletionRepository repository = new(context);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.SaveSimulationAndCompleteAsync(
                        jobId,
                        ClaimToken,
                        Simulation,
                        CancellationToken.None));

            Assert.Contains("pending tracked changes", exception.Message);
            Assert.Same(
                callerTransaction,
                context.Database.CurrentTransaction);
            Assert.Equal(EntityState.Modified, context.Entry(callerEntity).State);

            SimulationJobEntity job =
                await context.SimulationJobs
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == jobId);
            Assert.Equal(SimulationJobStatus.Running, job.Status);
            Assert.Null(job.SimulationId);
            Assert.Equal(ClaimToken, job.ClaimToken);
            Assert.NotNull(job.LeaseExpiresAtUtc);

            SimulationEntity persistedSimulation =
                await context.Simulations
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == unrelatedSimulationId);
            Assert.Equal(3, persistedSimulation.SampleCount);
            Assert.Null(capture.SimulationId);
            Assert.Equal(0, capture.SimulationSaveInvocationCount);

            await transaction.RollbackAsync();
        }
        finally
        {
            await DeleteExactJobsAsync(seedOptions, jobId);
            await DeleteExactSimulationAsync(
                seedOptions,
                unrelatedSimulationId);
        }
    }

    [Fact]
    public async Task SaveSimulationAndCompleteAsync_RejectsEmptyJobIdBeforeConnecting()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql("Host=localhost;Database=singular_flow_tests")
                .Options;
        await using SingularFlowDbContext context = new(options);
        EfSimulationJobCompletionRepository repository = new(context);

        ArgumentException exception =
            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.SaveSimulationAndCompleteAsync(
                    Guid.Empty,
                    ClaimToken,
                    Simulation,
                    CancellationToken.None));

        Assert.Equal("jobId", exception.ParamName);
        Assert.Equal(ConnectionState.Closed, context.Database
            .GetDbConnection().State);
    }

    [Fact]
    public async Task SaveSimulationAndCompleteAsync_RejectsNullSimulationBeforeConnecting()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql("Host=localhost;Database=singular_flow_tests")
                .Options;
        await using SingularFlowDbContext context = new(options);
        EfSimulationJobCompletionRepository repository = new(context);

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repository.SaveSimulationAndCompleteAsync(
                    Guid.NewGuid(),
                    ClaimToken,
                    null!,
                    CancellationToken.None));

        Assert.Equal("simulation", exception.ParamName);
        Assert.Equal(ConnectionState.Closed, context.Database
            .GetDbConnection().State);
    }

    private static async Task<DbContextOptions<SingularFlowDbContext>>
        CreateProtectedOptionsAsync(
            SaveChangesInterceptor? interceptor = null)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(
                "SINGULARFLOW_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");

        NpgsqlConnectionStringBuilder connection = new(connectionString);
        Assert.Equal("singular_flow_tests", connection.Database);

        NpgsqlConnectionStringBuilder maintenanceConnection =
            new(connection.ConnectionString)
            {
                Database = "postgres"
            };

        await using (NpgsqlConnection maintenance =
            new(maintenanceConnection.ConnectionString))
        {
            await maintenance.OpenAsync();
            await using NpgsqlCommand existsCommand =
                new(
                    "SELECT EXISTS (" +
                    "SELECT 1 FROM pg_database " +
                    "WHERE datname = 'singular_flow_tests')",
                    maintenance);
            Assert.True((bool)(await existsCommand.ExecuteScalarAsync())!);
        }

        await using (NpgsqlConnection testDatabase =
            new(connection.ConnectionString))
        {
            await testDatabase.OpenAsync();
            await using NpgsqlCommand currentDatabaseCommand =
                new("SELECT current_database()", testDatabase);
            Assert.Equal(
                "singular_flow_tests",
                await currentDatabaseCommand.ExecuteScalarAsync());
        }

        DbContextOptionsBuilder<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(connection.ConnectionString);

        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return options.Options;
    }

    private static async Task EnsureDatabaseMigratedAsync(
        DbContextOptions<SingularFlowDbContext> options)
    {
        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
    }

    private static async Task<Guid> CreateRunningJobAsync(
        DbContextOptions<SingularFlowDbContext> options)
    {
        Guid jobId = Guid.NewGuid();
        await SeedJobAsync(
            options,
            CreateJob(jobId, SimulationJobStatus.Running));
        return jobId;
    }

    private static async Task SeedJobAsync(
        DbContextOptions<SingularFlowDbContext> options,
        SimulationJobEntity job)
    {
        await using SingularFlowDbContext context = new(options);
        context.SimulationJobs.Add(job);
        await context.SaveChangesAsync();
    }

    private static SimulationJobEntity CreateJob(
        Guid id,
        SimulationJobStatus status)
    {
        DateTimeOffset timestamp = GetTimestamp();

        return new SimulationJobEntity
        {
            Id = id,
            SingularTime = Request.SingularTime,
            ConcentrationExponent = Request.ConcentrationExponent,
            StartTime = Request.StartTime,
            EndTime = Request.EndTime,
            SampleCount = Request.SampleCount,
            SamplingMode = Request.SamplingMode,
            Status = status,
            CreatedAtUtc = timestamp,
            ClaimedAtUtc = status == SimulationJobStatus.Pending
                ? null
                : timestamp,
            ClaimToken = status == SimulationJobStatus.Running
                ? ClaimToken
                : null,
            LeaseExpiresAtUtc = status == SimulationJobStatus.Running
                ? DateTimeOffset.UtcNow.AddMinutes(5)
                : null,
            FinishedAtUtc = status is
                SimulationJobStatus.Completed or
                SimulationJobStatus.Failed
                ? timestamp
                : null,
            FailureMessage = status == SimulationJobStatus.Failed
                ? "Seeded failure."
                : null
        };
    }

    private static SimulationJobStatus GetStatus(string state)
    {
        return state switch
        {
            "Pending" => SimulationJobStatus.Pending,
            "Completed" => SimulationJobStatus.Completed,
            "Failed" => SimulationJobStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }

    private static DateTimeOffset GetTimestamp() =>
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static async Task SeedSimulationAsync(
        DbContextOptions<SingularFlowDbContext> options,
        Guid simulationId)
    {
        await using SingularFlowDbContext context = new(options);
        context.Simulations.Add(
            new SimulationEntity
            {
                Id = simulationId,
                SingularTime = 1.0,
                ConcentrationExponent = 0.004,
                StartTime = 0.1,
                EndTime = 0.5,
                SampleCount = 3,
                SamplingMode = SamplingMode.Uniform
            });
        await context.SaveChangesAsync();
    }

    private static async Task DeleteExactJobsAsync(
        DbContextOptions<SingularFlowDbContext> options,
        Guid jobId)
    {
        await using SingularFlowDbContext context = new(options);
        await context.SimulationJobs
            .Where(job => job.Id == jobId)
            .ExecuteDeleteAsync();
        Assert.False(
            await context.SimulationJobs.AnyAsync(job => job.Id == jobId));
    }

    private static async Task DeleteExactSimulationAsync(
        DbContextOptions<SingularFlowDbContext> options,
        Guid simulationId)
    {
        await using SingularFlowDbContext context = new(options);
        await context.Simulations
            .Where(simulation => simulation.Id == simulationId)
            .ExecuteDeleteAsync();
        Assert.False(
            await context.Simulations.AnyAsync(
                simulation => simulation.Id == simulationId));
        Assert.False(
            await context.SimulationStates.AnyAsync(
                state => state.SimulationId == simulationId));
    }

    private static RunSimulationResult CreateSimulation()
    {
        return new RunSimulationResult(
            new BlowupParameters(2.25, 0.003),
            new TimeSeriesParameters(0.1, 0.5, 3),
            SamplingMode.Logarithmic,
            [
                new BlowupState(
                    0.1, 2.15, 0.03, 0.02, 1.2, 0.8, 0.0004, 0.002),
                new BlowupState(
                    0.3, 1.95, 0.04, 0.025, 1.4, 0.9, 0.0005, 0.003),
                new BlowupState(
                    0.5, 1.75, 0.05, 0.03, 1.6, 1.0, 0.0006, 0.004)
            ]);
    }

    private sealed class SimulationIdCaptureInterceptor :
        SaveChangesInterceptor
    {
        public Guid? SimulationId { get; private set; }

        public int SimulationSaveInvocationCount { get; private set; }

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            Guid? addedSimulationId =
                eventData.Context?.ChangeTracker
                    .Entries<SimulationEntity>()
                    .SingleOrDefault(entry =>
                        entry.State == EntityState.Added)
                    ?.Entity.Id;
            if (addedSimulationId is Guid id)
            {
                SimulationId = id;
                SimulationSaveInvocationCount++;
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class InvalidSimulationStateSequenceInterceptor :
        SaveChangesInterceptor
    {
        public Guid? SimulationId { get; private set; }

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            SimulationEntity? simulation =
                eventData.Context?.ChangeTracker
                    .Entries<SimulationEntity>()
                    .SingleOrDefault(entry =>
                        entry.State == EntityState.Added)
                    ?.Entity;
            SimulationStateEntity? state =
                eventData.Context?.ChangeTracker
                    .Entries<SimulationStateEntity>()
                    .Where(entry => entry.State == EntityState.Added)
                    .OrderBy(entry => entry.Entity.Sequence)
                    .Select(entry => entry.Entity)
                    .FirstOrDefault();

            if (simulation is not null && state is not null)
            {
                SimulationId = simulation.Id;
                state.Sequence = -1;
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }
}