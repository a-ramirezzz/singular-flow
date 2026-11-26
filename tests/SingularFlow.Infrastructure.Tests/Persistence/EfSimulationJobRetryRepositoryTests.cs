using System.Data;
using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobRetryRepositoryTests
{
    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => CreateRepository(null!));

        ArgumentNullException inner =
            Assert.IsType<ArgumentNullException>(exception.InnerException);
        Assert.Equal("dbContext", inner.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryRecordRetryableFailureAsync_InvalidMessage_ThrowsBeforeConnectionAccess(
        string? message)
    {
        await using SingularFlowDbContext context = CreateUnconfiguredContext();
        ISimulationJobRetryRepository repository = CreateRepository(context);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => repository.TryRecordRetryableFailureAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                message!,
                TimeSpan.FromSeconds(1),
                CancellationToken.None));
    }

    [Fact]
    public async Task TryRecordRetryableFailureAsync_MessageTooLong_ThrowsBeforeConnectionAccess()
    {
        await using SingularFlowDbContext context = CreateUnconfiguredContext();
        ISimulationJobRetryRepository repository = CreateRepository(context);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.TryRecordRetryableFailureAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    new string('x', SimulationJobResult.MaximumFailureMessageLength + 1),
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None));

        Assert.Equal("safeFailureMessage", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(301)]
    public async Task TryRecordRetryableFailureAsync_InvalidDelay_ThrowsBeforeConnectionAccess(
        double seconds)
    {
        await using SingularFlowDbContext context = CreateUnconfiguredContext();
        ISimulationJobRetryRepository repository = CreateRepository(context);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.TryRecordRetryableFailureAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Safe failure.",
                    TimeSpan.FromSeconds(seconds),
                    CancellationToken.None));

        Assert.Equal("retryDelay", exception.ParamName);
    }

    [Fact]
    public async Task TryRecordRetryableFailureAsync_CurrentOwner_SchedulesRetryAndPreservesContract()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(Guid.NewGuid(), token, attemptCount: 1);
        await SeedRunningJobAsync(context, job);
        await SetActiveLeaseAsync(context, job.Id);
        context.ChangeTracker.Clear();
        ISimulationJobRetryRepository repository = CreateRepository(context);

        SimulationJobRetryDecision? decision =
            await repository.TryRecordRetryableFailureAsync(
                job.Id,
                token,
                "Transient persistence failure.",
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

        SimulationJobRetryDecision.Scheduled scheduled =
            Assert.IsType<SimulationJobRetryDecision.Scheduled>(decision);
        Assert.Equal(job.Id, scheduled.Job.Id);
        Assert.Equal(CreateRequest(), scheduled.Job.Request);
        Assert.Equal(SimulationJobStatus.Pending, scheduled.Job.Status);
        Assert.Null(scheduled.Job.ClaimedAtUtc);
        Assert.Null(scheduled.Job.SimulationId);
        Assert.Null(scheduled.Job.FinishedAtUtc);
        Assert.Null(scheduled.Job.FailureMessage);
        Assert.Equal(1, scheduled.Job.AttemptCount);
        Assert.Equal(3, scheduled.Job.MaxAttempts);
        Assert.NotNull(scheduled.Job.NextAttemptAtUtc);
        Assert.NotNull(scheduled.Job.LastFailedAtUtc);
        Assert.Equal("Transient persistence failure.", scheduled.Job.LastFailureMessage);

        SimulationJobEntity persisted = await context.SimulationJobs.AsNoTracking()
            .SingleAsync(item => item.Id == job.Id);
        Assert.Equal(SimulationJobStatus.Pending, persisted.Status);
        Assert.Null(persisted.ClaimedAtUtc);
        Assert.Null(persisted.ClaimToken);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.True(persisted.NextAttemptAtUtc > persisted.LastFailedAtUtc);
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TryRecordRetryableFailureAsync_FinalAttempt_ExhaustsToFailed()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(Guid.NewGuid(), token, attemptCount: 3);
        await SeedRunningJobAsync(context, job);
        await SetActiveLeaseAsync(context, job.Id);
        context.ChangeTracker.Clear();

        SimulationJobRetryDecision? decision =
            await CreateRepository(context).TryRecordRetryableFailureAsync(
                job.Id,
                token,
                "Retry budget exhausted.",
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

        SimulationJobRetryDecision.Exhausted exhausted =
            Assert.IsType<SimulationJobRetryDecision.Exhausted>(decision);
        Assert.Equal(SimulationJobStatus.Failed, exhausted.Job.Status);
        Assert.NotNull(exhausted.Job.ClaimedAtUtc);
        Assert.NotNull(exhausted.Job.FinishedAtUtc);
        Assert.Equal("Retry budget exhausted.", exhausted.Job.FailureMessage);
        Assert.Equal(3, exhausted.Job.AttemptCount);
        Assert.Equal(3, exhausted.Job.MaxAttempts);
        Assert.Null(exhausted.Job.NextAttemptAtUtc);
        Assert.NotNull(exhausted.Job.LastFailedAtUtc);
        Assert.Equal("Retry budget exhausted.", exhausted.Job.LastFailureMessage);

        SimulationJobEntity persisted = await context.SimulationJobs.AsNoTracking()
            .SingleAsync(item => item.Id == job.Id);
        Assert.Equal(SimulationJobStatus.Failed, persisted.Status);
        Assert.Null(persisted.ClaimToken);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.NotNull(persisted.FinishedAtUtc);
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TryRecordRetryableFailureAsync_StaleExpiredOrIneligibleOwner_ReturnsNullWithoutMutation()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid activeToken = Guid.NewGuid();
        SimulationJobEntity active = CreateRunningJob(Guid.NewGuid(), activeToken, attemptCount: 1);
        SimulationJobEntity expired = CreateRunningJob(Guid.NewGuid(), Guid.NewGuid(), attemptCount: 1);
        SimulationJobEntity pending = CreatePendingJob(Guid.NewGuid());
        await SeedRunningJobAsync(context, active);
        await SeedRunningJobAsync(context, expired);
        context.SimulationJobs.Add(pending);
        await context.SaveChangesAsync();
        await SetActiveLeaseAsync(context, active.Id);
        await SetExpiredLeaseAsync(context, expired.Id);
        context.ChangeTracker.Clear();
        ISimulationJobRetryRepository repository = CreateRepository(context);

        Assert.Null(await repository.TryRecordRetryableFailureAsync(
            active.Id, Guid.NewGuid(), "Safe failure.", TimeSpan.FromSeconds(1), default));
        Assert.Null(await repository.TryRecordRetryableFailureAsync(
            expired.Id, expired.ClaimToken!.Value, "Safe failure.", TimeSpan.FromSeconds(1), default));
        Assert.Null(await repository.TryRecordRetryableFailureAsync(
            pending.Id, activeToken, "Safe failure.", TimeSpan.FromSeconds(1), default));
        Assert.Null(await repository.TryRecordRetryableFailureAsync(
            Guid.NewGuid(), activeToken, "Safe failure.", TimeSpan.FromSeconds(1), default));

        SimulationJobEntity persisted = await context.SimulationJobs.AsNoTracking()
            .SingleAsync(item => item.Id == active.Id);
        Assert.Equal(SimulationJobStatus.Running, persisted.Status);
        Assert.Equal(activeToken, persisted.ClaimToken);
        Assert.Null(persisted.LastFailedAtUtc);
        Assert.Null(persisted.LastFailureMessage);
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TryRecordRetryableFailureAsync_ClosedConnection_RestoresClosedState()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        Guid id = Guid.NewGuid();
        Guid token = Guid.NewGuid();
        await using (SingularFlowDbContext seedContext = new(options))
        {
            await SeedRunningJobAsync(
                seedContext,
                CreateRunningJob(id, token, attemptCount: 1));
            await SetActiveLeaseAsync(seedContext, id);
        }

        try
        {
            await using SingularFlowDbContext context = new(options);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);

            SimulationJobRetryDecision? decision =
                await CreateRepository(context).TryRecordRetryableFailureAsync(
                    id,
                    token,
                    "Transient persistence failure.",
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);

            Assert.IsType<SimulationJobRetryDecision.Scheduled>(decision);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            await using SingularFlowDbContext cleanupContext = new(options);
            await cleanupContext.SimulationJobs
                .Where(item => item.Id == id)
                .ExecuteDeleteAsync();
        }
    }

    private static SingularFlowDbContext CreateUnconfiguredContext() =>
        new(new DbContextOptionsBuilder<SingularFlowDbContext>().Options);

    private static DbContextOptions<SingularFlowDbContext> GetOptions()
    {
        string value = Environment.GetEnvironmentVariable(
            "SINGULARFLOW_TEST_CONNECTION_STRING") ??
            throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");
        NpgsqlConnectionStringBuilder builder = new(value);
        Assert.Equal("singular_flow_tests", builder.Database);
        return new DbContextOptionsBuilder<SingularFlowDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;
    }

    private static RunSimulationRequest CreateRequest() =>
        new(2.5, 0.004, 0.25, 1.75, 17, SamplingMode.Logarithmic);

    private static SimulationJobEntity CreateRunningJob(
        Guid id,
        Guid claimToken,
        int attemptCount)
    {
        DateTimeOffset timestamp = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        return new SimulationJobEntity
        {
            Id = id,
            SingularTime = 2.5,
            ConcentrationExponent = 0.004,
            StartTime = 0.25,
            EndTime = 1.75,
            SampleCount = 17,
            SamplingMode = SamplingMode.Logarithmic,
            Status = SimulationJobStatus.Running,
            CreatedAtUtc = timestamp,
            ClaimedAtUtc = timestamp,
            ClaimToken = claimToken,
            LeaseExpiresAtUtc = timestamp.AddYears(2),
            AttemptCount = attemptCount,
            MaxAttempts = 3
        };
    }

    private static SimulationJobEntity CreatePendingJob(Guid id)
    {
        DateTimeOffset timestamp = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        return new SimulationJobEntity
        {
            Id = id,
            SingularTime = 2.5,
            ConcentrationExponent = 0.004,
            StartTime = 0.25,
            EndTime = 1.75,
            SampleCount = 17,
            SamplingMode = SamplingMode.Logarithmic,
            Status = SimulationJobStatus.Pending,
            CreatedAtUtc = timestamp,
            AttemptCount = 0,
            MaxAttempts = 3,
            NextAttemptAtUtc = timestamp
        };
    }

    private static Task<int> SetActiveLeaseAsync(
        SingularFlowDbContext context,
        Guid id) =>
        context.Database.ExecuteSqlRawAsync(
            "UPDATE simulation_jobs SET claimed_at_utc = CURRENT_TIMESTAMP, lease_expires_at_utc = CURRENT_TIMESTAMP + interval '1 minute' WHERE id = {0}",
            id);

    private static Task<int> SeedRunningJobAsync(
        SingularFlowDbContext context,
        SimulationJobEntity job) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO simulation_jobs
            (
                id,
                singular_time,
                concentration_exponent,
                start_time,
                end_time,
                sample_count,
                sampling_mode,
                status,
                created_at_utc,
                claimed_at_utc,
                claim_token,
                lease_expires_at_utc,
                attempt_count,
                max_attempts,
                next_attempt_at_utc,
                last_failed_at_utc,
                last_failure_message,
                simulation_id,
                finished_at_utc,
                failure_message
            )
            VALUES
            (
                {job.Id},
                {job.SingularTime},
                {job.ConcentrationExponent},
                {job.StartTime},
                {job.EndTime},
                {job.SampleCount},
                {nameof(SamplingMode.Logarithmic)},
                {nameof(SimulationJobStatus.Running)},
                {job.CreatedAtUtc},
                {job.ClaimedAtUtc},
                {job.ClaimToken},
                {job.LeaseExpiresAtUtc},
                {job.AttemptCount},
                {job.MaxAttempts},
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL
            )
            """);

    private static Task<int> SetExpiredLeaseAsync(
        SingularFlowDbContext context,
        Guid id) =>
        context.Database.ExecuteSqlRawAsync(
            "UPDATE simulation_jobs SET claimed_at_utc = CURRENT_TIMESTAMP - interval '2 minutes', lease_expires_at_utc = CURRENT_TIMESTAMP - interval '1 minute' WHERE id = {0}",
            id);

    private static ISimulationJobRetryRepository CreateRepository(
        SingularFlowDbContext context)
    {
        Type type = typeof(SingularFlowDbContext).Assembly.GetType(
            "SingularFlow.Infrastructure.Persistence.EfSimulationJobRetryRepository",
            throwOnError: true)!;

        return Assert.IsAssignableFrom<ISimulationJobRetryRepository>(
            Activator.CreateInstance(type, context));
    }
}