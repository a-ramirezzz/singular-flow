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
        context.AddRange(simulation, job);
        await context.SaveChangesAsync();
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
        context.Add(job);
        await context.SaveChangesAsync();
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
        context.AddRange(simulation, job);
        if (scenario == "other-job-token")
        {
            context.Add(CreateRunningJob(Guid.NewGuid()));
        }

        await context.SaveChangesAsync();
        if (status == SimulationJobStatus.Running)
        {
            await SetLeaseAsync(
                context,
                job.Id,
                scenario.StartsWith("expired", StringComparison.Ordinal));
        }

        context.ChangeTracker.Clear();
        SimulationJobEntity before = await ReadJobAsync(context, job.Id);
        Guid suppliedToken = scenario switch
        {
            "wrong-complete" or "wrong-fail" => Guid.NewGuid(),
            "empty-complete" or "empty-fail" => Guid.Empty,
            "other-job-token" => await context.SimulationJobs
                .Where(item => item.Id != job.Id)
                .Select(item => item.ClaimToken!.Value)
                .SingleAsync(),
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
        context.AddRange(simulation, job);
        await context.SaveChangesAsync();
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
        context.Add(job);
        await context.SaveChangesAsync();
        await SetLeaseAsync(context, job.Id, expired: true);
        context.ChangeTracker.Clear();
        EfSimulationJobLeaseRepository leases = new(context);

        Assert.Contains(job.Id, await leases.RecoverExpiredAsync(1, default));
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
            seed.AddRange(simulation, job);
            await seed.SaveChangesAsync();
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

    private static Task SetLeaseAsync(
        SingularFlowDbContext context,
        Guid id,
        bool expired) =>
        context.Database.ExecuteSqlRawAsync(
            expired
                ? "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP - interval '1 second' WHERE id = {0}"
                : "UPDATE simulation_jobs SET lease_expires_at_utc = CURRENT_TIMESTAMP + interval '1 minute' WHERE id = {0}",
            id);

    private static Task<SimulationJobEntity> ReadJobAsync(
        SingularFlowDbContext context,
        Guid id) =>
        context.SimulationJobs.AsNoTracking().SingleAsync(job => job.Id == id);
}