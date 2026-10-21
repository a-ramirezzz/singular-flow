using System.Data;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobLeaseRepositoryTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new EfSimulationJobLeaseRepository(null!));

        Assert.Equal("context", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ClaimNextPendingAsync_NonPositiveDuration_ThrowsBeforeConnectionAccess(
        double seconds)
    {
        await using SingularFlowDbContext context = CreateUnconfiguredContext();
        EfSimulationJobLeaseRepository repository = new(context);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.ClaimNextPendingAsync(
                    TimeSpan.FromSeconds(seconds),
                    CancellationToken.None));

        Assert.Equal("leaseDuration", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TryRenewLeaseAsync_NonPositiveDuration_ThrowsBeforeConnectionAccess(
        double seconds)
    {
        await using SingularFlowDbContext context = CreateUnconfiguredContext();
        EfSimulationJobLeaseRepository repository = new(context);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.TryRenewLeaseAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    TimeSpan.FromSeconds(seconds),
                    CancellationToken.None));

        Assert.Equal("leaseDuration", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecoverExpiredAsync_NonPositiveBatchSize_ThrowsBeforeConnectionAccess(
        int batchSize)
    {
        await using SingularFlowDbContext context = CreateUnconfiguredContext();
        EfSimulationJobLeaseRepository repository = new(context);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.RecoverExpiredAsync(
                    batchSize,
                    CancellationToken.None));

        Assert.Equal("batchSize", exception.ParamName);
    }

    [Fact]
    public async Task EmptyOperations_ReturnNullAndEmptyWithoutTrackingOrChangingCounts()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await context.Database.CloseConnectionAsync();
        DatabaseCounts before = await GetCountsAsync(context);
        await context.Database.CloseConnectionAsync();
        EfSimulationJobLeaseRepository repository = new(context);

        SimulationJobClaim? claim = await repository.ClaimNextPendingAsync(
            LeaseDuration,
            CancellationToken.None);
        IReadOnlyList<Guid> recovered = await repository.RecoverExpiredAsync(
            5,
            CancellationToken.None);

        Assert.Null(claim);
        Assert.NotNull(recovered);
        Assert.Empty(recovered);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(before, await GetCountsAsync(context));
    }

    [Fact]
    public async Task ClaimNextPendingAsync_PendingJob_ReturnsCompleteLeaseAndRollsBack()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        SimulationJobEntity seeded = CreateJob(Guid.NewGuid(), SimulationJobStatus.Pending);
        context.SimulationJobs.Add(seeded);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        EfSimulationJobLeaseRepository repository = new(context);

        SimulationJobClaim? claim = await repository.ClaimNextPendingAsync(
            LeaseDuration,
            CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal(seeded.Id, claim.Job.Id);
        Assert.Equal(CreateRequest(), claim.Job.Request);
        Assert.Equal(SimulationJobStatus.Running, claim.Job.Status);
        Assert.Equal(seeded.CreatedAtUtc, claim.Job.CreatedAtUtc);
        Assert.NotNull(claim.Job.ClaimedAtUtc);
        Assert.NotEqual(Guid.Empty, claim.ClaimToken);
        Assert.True(claim.LeaseExpiresAtUtc > claim.Job.ClaimedAtUtc);
        Assert.Null(claim.Job.SimulationId);
        Assert.Null(claim.Job.FinishedAtUtc);
        Assert.Null(claim.Job.FailureMessage);
        Assert.Empty(context.ChangeTracker.Entries());
        SimulationJobEntity persisted = await context.SimulationJobs
            .AsNoTracking().SingleAsync(job => job.Id == seeded.Id);
        Assert.Equal(claim.ClaimToken, persisted.ClaimToken);
        Assert.Equal(claim.LeaseExpiresAtUtc, persisted.LeaseExpiresAtUtc);
        Assert.Equal(claim.Job.ClaimedAtUtc, persisted.ClaimedAtUtc);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ClaimNextPendingAsync_OrdersPendingAndIgnoresOtherStatuses()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        DateTimeOffset created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Guid lower = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid higher = Guid.Parse("00000000-0000-0000-0000-000000000002");
        context.SimulationJobs.AddRange(
            CreateJob(higher, SimulationJobStatus.Pending, created),
            CreateJob(lower, SimulationJobStatus.Pending, created),
            CreateJob(Guid.NewGuid(), SimulationJobStatus.Running, created.AddDays(-1)),
            CreateJob(Guid.NewGuid(), SimulationJobStatus.Completed, created.AddDays(-1)),
            CreateJob(Guid.NewGuid(), SimulationJobStatus.Failed, created.AddDays(-1)),
            CreateJob(Guid.NewGuid(), SimulationJobStatus.Cancelled, created.AddDays(-1)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        SimulationJobClaim? claim = await new EfSimulationJobLeaseRepository(context)
            .ClaimNextPendingAsync(LeaseDuration, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal(lower, claim.Job.Id);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ClaimNextPendingAsync_ConcurrentCallersClaimDistinctJobsAndTokens()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        await SeedExactAsync(options,
            CreateJob(ids[0], SimulationJobStatus.Pending),
            CreateJob(ids[1], SimulationJobStatus.Pending));

        try
        {
            await using SingularFlowDbContext firstContext = new(options);
            await using SingularFlowDbContext secondContext = new(options);
            await firstContext.Database.OpenConnectionAsync();
            await secondContext.Database.OpenConnectionAsync();
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SimulationJobClaim?> first = ClaimAfterGateAsync(
                new(firstContext), firstReady, gate.Task);
            Task<SimulationJobClaim?> second = ClaimAfterGateAsync(
                new(secondContext), secondReady, gate.Task);
            await Task.WhenAll(firstReady.Task, secondReady.Task);
            gate.SetResult();

            SimulationJobClaim?[] claims = await Task.WhenAll(first, second);

            Assert.All(claims, Assert.NotNull);
            Assert.Equal(2, claims.Select(item => item!.Job.Id).Distinct().Count());
            Assert.Equal(2, claims.Select(item => item!.ClaimToken).Distinct().Count());
            Assert.All(claims, item => Assert.NotEqual(Guid.Empty, item!.ClaimToken));
            Assert.Equal(ConnectionState.Open, firstContext.Database.GetDbConnection().State);
            Assert.Equal(ConnectionState.Open, secondContext.Database.GetDbConnection().State);
        }
        finally
        {
            await DeleteExactAsync(options, ids);
        }
    }

    [Fact]
    public async Task ClaimNextPendingAsync_UsesExistingTransactionAndRollbackRestoresRow()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        SimulationJobEntity job = CreateJob(Guid.NewGuid(), SimulationJobStatus.Pending);
        context.SimulationJobs.Add(job);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        SimulationJobClaim? result = await new EfSimulationJobLeaseRepository(context)
            .ClaimNextPendingAsync(LeaseDuration, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(job.Id, result.Job.Id);
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TryRenewLeaseAsync_OnlyCurrentUnexpiredOwnerRenewsWithoutChangingClaim()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateJob(Guid.NewGuid(), SimulationJobStatus.Running);
        job.ClaimToken = token;
        job.LeaseExpiresAtUtc = job.ClaimedAtUtc!.Value.AddMinutes(1);
        context.SimulationJobs.Add(job);
        await context.SaveChangesAsync();
        await SetLeaseRelativeToDatabaseTimeAsync(
            context,
            job.Id,
            "+ interval '1 minute'");
        DateTimeOffset oldExpiration = await context.SimulationJobs
            .AsNoTracking()
            .Where(item => item.Id == job.Id)
            .Select(item => item.LeaseExpiresAtUtc!.Value)
            .SingleAsync();
        DateTimeOffset claimedAt = job.ClaimedAtUtc.Value;
        context.ChangeTracker.Clear();
        EfSimulationJobLeaseRepository repository = new(context);

        DateTimeOffset? renewed = await repository.TryRenewLeaseAsync(
            job.Id, token, LeaseDuration, CancellationToken.None);

        Assert.NotNull(renewed);
        Assert.True(renewed > oldExpiration);
        SimulationJobEntity persisted = await context.SimulationJobs.AsNoTracking()
            .SingleAsync(item => item.Id == job.Id);
        Assert.Equal(renewed, persisted.LeaseExpiresAtUtc);
        Assert.Equal(claimedAt, persisted.ClaimedAtUtc);
        Assert.Equal(token, persisted.ClaimToken);
        Assert.Equal(SimulationJobStatus.Running, persisted.Status);
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TryRenewLeaseAsync_RejectedOwnersAndStatesReturnNullWithoutMutation()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity active = CreateJob(Guid.NewGuid(), SimulationJobStatus.Running);
        active.ClaimToken = token;
        active.LeaseExpiresAtUtc = active.ClaimedAtUtc!.Value.AddMinutes(1);
        SimulationJobEntity expired = CreateJob(Guid.NewGuid(), SimulationJobStatus.Running);
        expired.ClaimToken = Guid.NewGuid();
        expired.LeaseExpiresAtUtc = expired.ClaimedAtUtc!.Value.AddSeconds(1);
        SimulationJobEntity pending = CreateJob(Guid.NewGuid(), SimulationJobStatus.Pending);
        context.SimulationJobs.AddRange(active, expired, pending);
        await context.SaveChangesAsync();
        await SetLeaseRelativeToDatabaseTimeAsync(context, expired.Id, "- interval '1 second'");
        context.ChangeTracker.Clear();
        EfSimulationJobLeaseRepository repository = new(context);

        Assert.Null(await repository.TryRenewLeaseAsync(active.Id, Guid.NewGuid(), LeaseDuration, default));
        Assert.Null(await repository.TryRenewLeaseAsync(active.Id, Guid.Empty, LeaseDuration, default));
        Assert.Null(await repository.TryRenewLeaseAsync(Guid.NewGuid(), token, LeaseDuration, default));
        Assert.Null(await repository.TryRenewLeaseAsync(expired.Id, expired.ClaimToken!.Value, LeaseDuration, default));
        Assert.Null(await repository.TryRenewLeaseAsync(pending.Id, token, LeaseDuration, default));
        SimulationJobEntity persisted = await context.SimulationJobs.AsNoTracking()
            .SingleAsync(item => item.Id == active.Id);
        Assert.Equal(active.LeaseExpiresAtUtc, persisted.LeaseExpiresAtUtc);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task RecoverExpiredAsync_IsBoundedOrderedAndPreservesNoneligibleRows()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Guid third = Guid.Parse("00000000-0000-0000-0000-000000000003");
        SimulationJobEntity[] jobs =
        [
            CreateJob(first, SimulationJobStatus.Running),
            CreateJob(second, SimulationJobStatus.Running),
            CreateJob(third, SimulationJobStatus.Running),
            CreateJob(Guid.NewGuid(), SimulationJobStatus.Running),
            CreateJob(Guid.NewGuid(), SimulationJobStatus.Pending),
            CreateJob(Guid.NewGuid(), SimulationJobStatus.Completed)
        ];
        context.SimulationJobs.AddRange(jobs);
        await context.SaveChangesAsync();
        await SetLeaseRelativeToDatabaseTimeAsync(context, first, "- interval '2 minutes'");
        await SetLeaseRelativeToDatabaseTimeAsync(context, second, "- interval '2 minutes'");
        await SetLeaseRelativeToDatabaseTimeAsync(context, third, "- interval '1 minute'");
        await SetLeaseRelativeToDatabaseTimeAsync(context, jobs[3].Id, "+ interval '1 minute'");
        context.ChangeTracker.Clear();

        IReadOnlyList<Guid> recovered = await new EfSimulationJobLeaseRepository(context)
            .RecoverExpiredAsync(2, CancellationToken.None);

        Assert.Equal([first, second], recovered);
        SimulationJobEntity[] persisted = await context.SimulationJobs.AsNoTracking()
            .Where(job => recovered.Contains(job.Id)).ToArrayAsync();
        Assert.All(persisted, job =>
        {
            SimulationJobEntity original = jobs.Single(item => item.Id == job.Id);
            Assert.Equal(SimulationJobStatus.Pending, job.Status);
            Assert.Null(job.ClaimedAtUtc);
            Assert.Null(job.ClaimToken);
            Assert.Null(job.LeaseExpiresAtUtc);
            Assert.Equal(CreateRequest(), ToRequest(job));
            Assert.Equal(original.Id, job.Id);
            Assert.Equal(original.CreatedAtUtc, job.CreatedAtUtc);
        });
        Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task RecoverExpiredAsync_ConcurrentCallersRecoverDisjointBoundedSets()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await SeedExactAsync(options, ids.Select(id => CreateJob(id, SimulationJobStatus.Running)).ToArray());
        await SetExactLeasesExpiredAsync(options, ids);

        try
        {
            await using SingularFlowDbContext firstContext = new(options);
            await using SingularFlowDbContext secondContext = new(options);
            await firstContext.Database.OpenConnectionAsync();
            await secondContext.Database.OpenConnectionAsync();
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<IReadOnlyList<Guid>> first = RecoverAfterGateAsync(new(firstContext), firstReady, gate.Task);
            Task<IReadOnlyList<Guid>> second = RecoverAfterGateAsync(new(secondContext), secondReady, gate.Task);
            await Task.WhenAll(firstReady.Task, secondReady.Task);
            gate.SetResult();

            IReadOnlyList<Guid>[] results = await Task.WhenAll(first, second);
            Guid[] combined = results.SelectMany(item => item).ToArray();
            Assert.Equal(4, combined.Length);
            Assert.Equal(4, combined.Distinct().Count());
            Assert.Equal(ids.Order(), combined.Order());
        }
        finally
        {
            await DeleteExactAsync(options, ids);
        }
    }

    [Fact]
    public async Task ExpiredClaim_CanBeRecoveredAndReclaimedWithNewToken()
    {
        DbContextOptions<SingularFlowDbContext> options = GetOptions();
        await using SingularFlowDbContext context = new(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        SimulationJobEntity job = CreateJob(Guid.NewGuid(), SimulationJobStatus.Pending);
        context.SimulationJobs.Add(job);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        EfSimulationJobLeaseRepository repository = new(context);
        SimulationJobClaim first = Assert.IsType<SimulationJobClaim>(
            await repository.ClaimNextPendingAsync(LeaseDuration, default));
        await SetLeaseRelativeToDatabaseTimeAsync(context, job.Id, "- interval '1 second'");

        Assert.Equal([job.Id], await repository.RecoverExpiredAsync(1, default));
        SimulationJobClaim second = Assert.IsType<SimulationJobClaim>(
            await repository.ClaimNextPendingAsync(LeaseDuration, default));

        Assert.NotEqual(first.ClaimToken, second.ClaimToken);
        Assert.True(second.Job.ClaimedAtUtc >= first.Job.ClaimedAtUtc);
        Assert.True(second.LeaseExpiresAtUtc > second.Job.ClaimedAtUtc);
        Assert.Equal(first.Job.Request, second.Job.Request);
        Assert.Equal(first.Job.CreatedAtUtc, second.Job.CreatedAtUtc);
        await transaction.RollbackAsync();
    }

    private static async Task<SimulationJobClaim?> ClaimAfterGateAsync(
        EfSimulationJobLeaseRepository repository, TaskCompletionSource ready, Task gate)
    {
        ready.SetResult();
        await gate;
        return await repository.ClaimNextPendingAsync(LeaseDuration, default);
    }

    private static async Task<IReadOnlyList<Guid>> RecoverAfterGateAsync(
        EfSimulationJobLeaseRepository repository, TaskCompletionSource ready, Task gate)
    {
        ready.SetResult();
        await gate;
        return await repository.RecoverExpiredAsync(2, default);
    }

    private static SingularFlowDbContext CreateUnconfiguredContext() =>
        new(new DbContextOptionsBuilder<SingularFlowDbContext>().Options);

    private static DbContextOptions<SingularFlowDbContext> GetOptions()
    {
        string value = Environment.GetEnvironmentVariable("SINGULARFLOW_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set SINGULARFLOW_TEST_CONNECTION_STRING.");
        NpgsqlConnectionStringBuilder builder = new(value);
        Assert.Equal("singular_flow_tests", builder.Database);
        return new DbContextOptionsBuilder<SingularFlowDbContext>()
            .UseNpgsql(builder.ConnectionString).Options;
    }

    private static RunSimulationRequest CreateRequest() =>
        new(2.5, 0.004, 0.25, 1.75, 17, SamplingMode.Logarithmic);

    private static RunSimulationRequest ToRequest(SimulationJobEntity job) =>
        new(job.SingularTime, job.ConcentrationExponent, job.StartTime,
            job.EndTime, job.SampleCount, job.SamplingMode);

    private static SimulationJobEntity CreateJob(
        Guid id, SimulationJobStatus status, DateTimeOffset? createdAt = null)
    {
        DateTimeOffset timestamp = createdAt ?? new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        bool running = status == SimulationJobStatus.Running;
        bool terminal = status is SimulationJobStatus.Completed or SimulationJobStatus.Failed;
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
            CreatedAtUtc = timestamp,
            ClaimedAtUtc = running || terminal || status == SimulationJobStatus.Cancelled ? timestamp : null,
            ClaimToken = running ? Guid.NewGuid() : null,
            LeaseExpiresAtUtc = running ? timestamp.AddYears(2) : null,
            FinishedAtUtc = terminal ? timestamp.AddSeconds(1) : null,
            FailureMessage = status == SimulationJobStatus.Failed ? "Seeded failure" : null
        };
    }

    private static async Task SetLeaseRelativeToDatabaseTimeAsync(
        SingularFlowDbContext context, Guid id, string expression)
    {
        string sql = expression switch
        {
            "- interval '2 minutes'" =>
                "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP - interval '2 minutes' WHERE id = {0}",
            "- interval '1 minute'" =>
                "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP - interval '1 minute' WHERE id = {0}",
            "- interval '1 second'" =>
                "UPDATE simulation_jobs SET claimed_at_utc = CURRENT_TIMESTAMP - interval '2 seconds', lease_expires_at_utc = CURRENT_TIMESTAMP - interval '1 second' WHERE id = {0}",
            "+ interval '1 minute'" =>
                "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP + interval '1 minute' WHERE id = {0}",
            _ => throw new ArgumentOutOfRangeException(nameof(expression))
        };

        await context.Database.ExecuteSqlRawAsync(sql, id);
    }

    private static async Task SetExactLeasesExpiredAsync(
        DbContextOptions<SingularFlowDbContext> options, Guid[] ids)
    {
        await using SingularFlowDbContext context = new(options);
        for (int index = 0; index < ids.Length; index++)
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP - interval '1 minute' WHERE id = {0}",
                ids[index]);
        }
    }

    private static async Task SeedExactAsync(
        DbContextOptions<SingularFlowDbContext> options, params SimulationJobEntity[] jobs)
    {
        await using SingularFlowDbContext context = new(options);
        context.SimulationJobs.AddRange(jobs);
        await context.SaveChangesAsync();
    }

    private static async Task DeleteExactAsync(
        DbContextOptions<SingularFlowDbContext> options, params Guid[] ids)
    {
        await using SingularFlowDbContext context = new(options);
        await context.SimulationJobs.Where(job => ids.Contains(job.Id)).ExecuteDeleteAsync();
        Assert.False(await context.SimulationJobs.AnyAsync(job => ids.Contains(job.Id)));
    }

    private static async Task<DatabaseCounts> GetCountsAsync(SingularFlowDbContext context) =>
        new(await context.Simulations.CountAsync(),
            await context.SimulationStates.CountAsync(),
            await context.SimulationJobs.CountAsync());

    private sealed record DatabaseCounts(int Simulations, int States, int Jobs);
}