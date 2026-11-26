using System.Data;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobLeaseTerminalRepositoryTests
{
    [Fact]
    public async Task CorrectTokenAndLease_CompleteAndClearOwnership()
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationEntity simulation = CreateSimulation();
        SimulationJobEntity job = CreateRunningJob(token);
        context.Add(simulation);
        await SeedJobAsync(context, job);
        await SetLeaseAsync(context, job.Id, expired: false);
        context.ChangeTracker.Clear();

        SimulationJobResult? result = await new EfSimulationJobRepository(context)
            .TryCompleteAsync(job.Id, token, simulation.Id, default);

        Assert.NotNull(result);
        Assert.Equal(SimulationJobStatus.Completed, result.Status);
        Assert.Equal(simulation.Id, result.SimulationId);
        Assert.NotNull(result.ClaimedAtUtc);
        Assert.NotNull(result.FinishedAtUtc);
        Assert.Null(result.FailureMessage);
        SimulationJobEntity persisted = await ReadJobAsync(context, job.Id);
        Assert.Equal(SimulationJobStatus.Completed, persisted.Status);
        Assert.Equal(simulation.Id, persisted.SimulationId);
        Assert.Null(persisted.ClaimToken);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CorrectTokenAndLease_FailAndClearOwnership()
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(token);
        await SeedJobAsync(context, job);
        await SetLeaseAsync(context, job.Id, expired: false);
        context.ChangeTracker.Clear();

        SimulationJobResult? result = await new EfSimulationJobRepository(context)
            .TryFailAsync(job.Id, token, "  Exact failure.  ", default);

        Assert.NotNull(result);
        Assert.Equal(SimulationJobStatus.Failed, result.Status);
        Assert.Equal("  Exact failure.  ", result.FailureMessage);
        Assert.Null(result.SimulationId);
        Assert.NotNull(result.ClaimedAtUtc);
        Assert.NotNull(result.FinishedAtUtc);
        SimulationJobEntity persisted = await ReadJobAsync(context, job.Id);
        Assert.Null(persisted.ClaimToken);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData("wrong-complete")]
    [InlineData("wrong-fail")]
    [InlineData("empty-complete")]
    [InlineData("empty-fail")]
    [InlineData("expired-complete")]
    [InlineData("expired-fail")]
    [InlineData("pending")]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("missing")]
    [InlineData("other-job-token")]
    public async Task InvalidOwnershipOrState_ReturnsNullAndLeavesJobUnchanged(
        string scenario)
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationEntity simulation = CreateSimulation();
        SimulationJobStatus status = scenario switch
        {
            "pending" => SimulationJobStatus.Pending,
            "completed" => SimulationJobStatus.Completed,
            "failed" => SimulationJobStatus.Failed,
            "cancelled" => SimulationJobStatus.Cancelled,
            _ => SimulationJobStatus.Running
        };
        SimulationJobEntity job = CreateJob(token, status);
        SimulationJobEntity? otherJob = null;
        context.Add(simulation);
        await SeedJobAsync(context, job);
        if (scenario == "other-job-token")
        {
            Guid otherToken;
            do
            {
                otherToken = Guid.NewGuid();
            }
            while (otherToken == token || otherToken == Guid.Empty);

            otherJob = CreateRunningJob(otherToken);
            Assert.NotEqual(job.Id, otherJob.Id);
            await SeedJobAsync(context, otherJob);
            await SetLeaseAsync(context, otherJob.Id, expired: false);
        }
        if (status == SimulationJobStatus.Running)
        {
            await SetLeaseAsync(
                context,
                job.Id,
                scenario.StartsWith("expired", StringComparison.Ordinal));
        }

        context.ChangeTracker.Clear();
        SimulationJobEntity before = await ReadJobAsync(context, job.Id);
        SimulationJobEntity? otherBefore = null;
        if (otherJob is not null)
        {
            otherBefore = await ReadJobAsync(context, otherJob.Id);
            Assert.Equal(SimulationJobStatus.Running, otherBefore.Status);
            Assert.NotNull(otherBefore.ClaimedAtUtc);
            Assert.True(otherBefore.ClaimToken.HasValue);
            Assert.NotEqual(Guid.Empty, otherBefore.ClaimToken.Value);
            Assert.NotEqual(token, otherBefore.ClaimToken.Value);
            Assert.NotNull(otherBefore.LeaseExpiresAtUtc);
            Assert.True(
                otherBefore.LeaseExpiresAtUtc > otherBefore.ClaimedAtUtc);
            Assert.InRange(
                otherBefore.AttemptCount,
                1,
                otherBefore.MaxAttempts);
            Assert.Null(otherBefore.NextAttemptAtUtc);
            Assert.Null(otherBefore.SimulationId);
            Assert.Null(otherBefore.FinishedAtUtc);
            Assert.Null(otherBefore.FailureMessage);
        }

        Guid suppliedToken = scenario switch
        {
            "wrong-complete" or "wrong-fail" => Guid.NewGuid(),
            "empty-complete" or "empty-fail" => Guid.Empty,
            "other-job-token" => otherBefore!.ClaimToken!.Value,
            _ => token
        };
        Guid requestedJobId = scenario == "missing" ? Guid.NewGuid() : job.Id;
        EfSimulationJobRepository repository = new(context);

        SimulationJobResult? result = scenario.EndsWith(
            "fail",
            StringComparison.Ordinal)
            ? await repository.TryFailAsync(
                requestedJobId,
                suppliedToken,
                "Failure",
                default)
            : await repository.TryCompleteAsync(
                requestedJobId,
                suppliedToken,
                simulation.Id,
                default);

        Assert.Null(result);
        SimulationJobEntity after = await ReadJobAsync(context, job.Id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.SimulationId, after.SimulationId);
        Assert.Equal(before.FinishedAtUtc, after.FinishedAtUtc);
        Assert.Equal(before.FailureMessage, after.FailureMessage);
        Assert.Equal(before.ClaimToken, after.ClaimToken);
        Assert.Equal(before.LeaseExpiresAtUtc, after.LeaseExpiresAtUtc);
        Assert.Equal(before.ClaimedAtUtc, after.ClaimedAtUtc);
        Assert.Equal(before.AttemptCount, after.AttemptCount);
        Assert.Equal(before.MaxAttempts, after.MaxAttempts);
        Assert.Equal(before.NextAttemptAtUtc, after.NextAttemptAtUtc);
        Assert.Equal(before.LastFailedAtUtc, after.LastFailedAtUtc);
        Assert.Equal(before.LastFailureMessage, after.LastFailureMessage);
        if (otherJob is not null && otherBefore is not null)
        {
            SimulationJobEntity otherAfter = await ReadJobAsync(
                context,
                otherJob.Id);
            Assert.Equal(otherBefore.Status, otherAfter.Status);
            Assert.Equal(otherBefore.SimulationId, otherAfter.SimulationId);
            Assert.Equal(otherBefore.FinishedAtUtc, otherAfter.FinishedAtUtc);
            Assert.Equal(otherBefore.FailureMessage, otherAfter.FailureMessage);
            Assert.Equal(otherBefore.ClaimedAtUtc, otherAfter.ClaimedAtUtc);
            Assert.Equal(otherBefore.ClaimToken, otherAfter.ClaimToken);
            Assert.Equal(
                otherBefore.LeaseExpiresAtUtc,
                otherAfter.LeaseExpiresAtUtc);
            Assert.Equal(otherBefore.AttemptCount, otherAfter.AttemptCount);
            Assert.Equal(otherBefore.MaxAttempts, otherAfter.MaxAttempts);
            Assert.Equal(otherBefore.NextAttemptAtUtc, otherAfter.NextAttemptAtUtc);
            Assert.Equal(otherBefore.LastFailedAtUtc, otherAfter.LastFailedAtUtc);
            Assert.Equal(
                otherBefore.LastFailureMessage,
                otherAfter.LastFailureMessage);
        }
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ExistingTransaction_RollbackRestoresJobAndKeepsConnectionOpen()
    {
        await using SingularFlowDbContext context = CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationEntity simulation = CreateSimulation();
        SimulationJobEntity job = CreateRunningJob(token);
        context.Add(simulation);
        await SeedJobAsync(context, job);
        await SetLeaseAsync(context, job.Id, expired: false);
        context.ChangeTracker.Clear();

        Assert.NotNull(await new EfSimulationJobRepository(context)
            .TryCompleteAsync(job.Id, token, simulation.Id, default));
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
        await transaction.RollbackAsync();
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task RecoveredAndReclaimedJob_RejectsOldOwnerAndAcceptsNewOwner()
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid ownerA = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(ownerA);
        await SeedJobAsync(context, job);
        await SetLeaseAsync(context, job.Id, expired: true);
        context.ChangeTracker.Clear();
        EfSimulationJobLeaseRepository leases =
            new(context, SimulationJobRetryPolicy.Default);

        Assert.Contains(job.Id, await leases.RecoverExpiredAsync(1, default));
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE simulation_jobs SET next_attempt_at_utc = CURRENT_TIMESTAMP WHERE id = {0}",
            job.Id);
        SimulationJobClaim ownerB = Assert.IsType<SimulationJobClaim>(
            await leases.ClaimNextPendingAsync(TimeSpan.FromMinutes(1), default));
        Assert.NotEqual(Guid.Empty, ownerB.ClaimToken);
        Assert.NotEqual(ownerA, ownerB.ClaimToken);
        EfSimulationJobRepository jobs = new(context);
        Assert.Null(await jobs.TryCompleteAsync(
            job.Id, ownerA, Guid.NewGuid(), default));
        Assert.Null(await jobs.TryFailAsync(
            job.Id, ownerA, "Old owner", default));

        SimulationJobResult? result = await jobs.TryFailAsync(
            job.Id, ownerB.ClaimToken, "New owner", default);

        Assert.NotNull(result);
        Assert.Equal("New owner", result.FailureMessage);
        SimulationJobEntity persisted = await ReadJobAsync(context, job.Id);
        Assert.Null(persisted.ClaimToken);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ConcurrentTerminalAttempts_ExactlyOneWins()
    {
        DbContextOptions<SingularFlowDbContext> options = CreateOptions();
        Guid token = Guid.NewGuid();
        SimulationEntity simulation = CreateSimulation();
        SimulationJobEntity job = CreateRunningJob(token);
        await using (SingularFlowDbContext seed = new(options))
        {
            seed.Add(simulation);
            await SeedJobAsync(seed, job);
            await SetLeaseAsync(seed, job.Id, expired: false);
        }

        try
        {
            await using SingularFlowDbContext completeContext = new(options);
            await using SingularFlowDbContext failContext = new(options);
            await completeContext.Database.OpenConnectionAsync();
            await failContext.Database.OpenConnectionAsync();
            TaskCompletionSource release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource completeReady = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource failReady = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource safety = new(TimeSpan.FromSeconds(10));
            Task<SimulationJobResult?> complete = RunAfterGateAsync(
                () => new EfSimulationJobRepository(completeContext)
                    .TryCompleteAsync(job.Id, token, simulation.Id, safety.Token),
                completeReady,
                release.Task);
            Task<SimulationJobResult?> fail = RunAfterGateAsync(
                () => new EfSimulationJobRepository(failContext)
                    .TryFailAsync(job.Id, token, "Concurrent failure", safety.Token),
                failReady,
                release.Task);
            await Task.WhenAll(completeReady.Task, failReady.Task);
            release.SetResult();

            SimulationJobResult?[] results = await Task.WhenAll(complete, fail);

            Assert.False(safety.IsCancellationRequested);
            Assert.Single(results, result => result is not null);
            Assert.Single(results, result => result is null);
            await using SingularFlowDbContext verification = new(options);
            SimulationJobEntity persisted = await ReadJobAsync(
                verification,
                job.Id);
            SimulationJobResult winner = results.Single(result => result is not null)!;
            Assert.Equal(winner.Status, persisted.Status);
            Assert.Equal(winner.SimulationId, persisted.SimulationId);
            Assert.Equal(winner.FailureMessage, persisted.FailureMessage);
            Assert.Null(persisted.ClaimToken);
            Assert.Null(persisted.LeaseExpiresAtUtc);
        }
        finally
        {
            await using SingularFlowDbContext cleanup = new(options);
            await cleanup.SimulationJobs.Where(item => item.Id == job.Id)
                .ExecuteDeleteAsync();
            await cleanup.Simulations.Where(item => item.Id == simulation.Id)
                .ExecuteDeleteAsync();
            Assert.False(await cleanup.SimulationJobs.AnyAsync(
                item => item.Id == job.Id));
            Assert.False(await cleanup.Simulations.AnyAsync(
                item => item.Id == simulation.Id));
        }
    }

    private static SingularFlowDbContext CreateContext()
        => new(CreateOptions());

    private static DbContextOptions<SingularFlowDbContext> CreateOptions()
    {
        string value = Environment.GetEnvironmentVariable(
            "SINGULARFLOW_TEST_CONNECTION_STRING") ??
            throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");
        NpgsqlConnectionStringBuilder builder = new(value);
        Assert.Equal("singular_flow_tests", builder.Database);
        return new DbContextOptionsBuilder<SingularFlowDbContext>()
            .UseNpgsql(builder.ConnectionString).Options;
    }

    private static async Task<SimulationJobResult?> RunAfterGateAsync(
        Func<Task<SimulationJobResult?>> action,
        TaskCompletionSource ready,
        Task release)
    {
        ready.SetResult();
        await release;
        return await action();
    }

    private static SimulationJobEntity CreateRunningJob(Guid token) =>
        CreateJob(token, SimulationJobStatus.Running);

    private static async Task SeedJobAsync(
        SingularFlowDbContext context,
        SimulationJobEntity job)
    {
        context.Add(new SimulationJobEntity
        {
            Id = job.Id,
            SingularTime = job.SingularTime,
            ConcentrationExponent = job.ConcentrationExponent,
            StartTime = job.StartTime,
            EndTime = job.EndTime,
            SampleCount = job.SampleCount,
            SamplingMode = job.SamplingMode,
            Status = SimulationJobStatus.Pending,
            CreatedAtUtc = job.CreatedAtUtc,
            MaxAttempts = job.MaxAttempts
        });
        await context.SaveChangesAsync();

        if (job.Status == SimulationJobStatus.Pending)
        {
            return;
        }

        int affectedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE simulation_jobs
            SET status = {job.Status.ToString()},
                claimed_at_utc = {job.ClaimedAtUtc},
                claim_token = {job.ClaimToken},
                lease_expires_at_utc = {job.LeaseExpiresAtUtc},
                attempt_count = {job.AttemptCount},
                max_attempts = {job.MaxAttempts},
                next_attempt_at_utc = NULL,
                finished_at_utc = {job.FinishedAtUtc},
                failure_message = {job.FailureMessage}
            WHERE id = {job.Id}
            """);
        Assert.Equal(1, affectedRows);
    }

    private static SimulationJobEntity CreateJob(
        Guid token,
        SimulationJobStatus status)
    {
        DateTimeOffset timestamp =
            new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        bool running = status == SimulationJobStatus.Running;
        bool terminal = status is SimulationJobStatus.Completed or
            SimulationJobStatus.Failed;
        return new SimulationJobEntity
        {
            Id = Guid.NewGuid(),
            SingularTime = 2.5,
            ConcentrationExponent = 0.004,
            StartTime = 0.25,
            EndTime = 1.75,
            SampleCount = 3,
            SamplingMode = SamplingMode.Uniform,
            Status = status,
            CreatedAtUtc = timestamp,
            ClaimedAtUtc = status == SimulationJobStatus.Pending
                ? null
                : timestamp,
            ClaimToken = running ? token : null,
            LeaseExpiresAtUtc = running ? timestamp.AddYears(2) : null,
            AttemptCount = status == SimulationJobStatus.Pending ? 0 : 1,
            MaxAttempts = SimulationJobRetryPolicy.Default.MaxAttempts,
            FinishedAtUtc = terminal ? timestamp.AddSeconds(1) : null,
            FailureMessage = status == SimulationJobStatus.Failed
                ? "Seeded failure"
                : null
        };
    }

    private static SimulationEntity CreateSimulation() => new()
    {
        Id = Guid.NewGuid(),
        SingularTime = 2.5,
        ConcentrationExponent = 0.004,
        StartTime = 0.25,
        EndTime = 1.75,
        SampleCount = 3,
        SamplingMode = SamplingMode.Uniform
    };

    private static async Task SetLeaseAsync(
        SingularFlowDbContext context,
        Guid id,
        bool expired)
    {
        int affectedRows = await context.Database.ExecuteSqlRawAsync(
            expired
                ? "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP - interval '1 second' WHERE id = {0}"
                : "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP + interval '1 minute' WHERE id = {0}",
            id);
        Assert.Equal(1, affectedRows);
    }

    private static Task<SimulationJobEntity> ReadJobAsync(
        SingularFlowDbContext context,
        Guid id) =>
        context.SimulationJobs.AsNoTracking().SingleAsync(job => job.Id == id);
}