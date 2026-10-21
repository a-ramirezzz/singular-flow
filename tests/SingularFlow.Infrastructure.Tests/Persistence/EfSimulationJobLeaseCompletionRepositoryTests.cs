using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Domain.Models;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobLeaseCompletionRepositoryTests
{
    [Fact]
    public async Task CorrectOwner_PersistsSimulationStatesAndCompletesJob()
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(token);
        context.Add(job);
        await context.SaveChangesAsync();
        await SetLeaseAsync(context, job.Id, expired: false);
        context.ChangeTracker.Clear();

        SimulationJobResult? result =
            await new EfSimulationJobCompletionRepository(context)
                .SaveSimulationAndCompleteAsync(
                    job.Id,
                    token,
                    CreateSimulation(),
                    default);

        Assert.NotNull(result);
        Assert.Equal(SimulationJobStatus.Completed, result.Status);
        Assert.NotNull(result.SimulationId);
        Assert.NotNull(result.FinishedAtUtc);
        Assert.Equal(3, await context.SimulationStates.CountAsync(
            state => state.SimulationId == result.SimulationId));
        int[] sequence = await context.SimulationStates
            .Where(state => state.SimulationId == result.SimulationId)
            .OrderBy(state => state.Sequence)
            .Select(state => state.Sequence)
            .ToArrayAsync();
        Assert.Equal(new[] { 0, 1, 2 }, sequence);
        SimulationJobEntity persisted = await ReadJobAsync(context, job.Id);
        Assert.Equal(result.SimulationId, persisted.SimulationId);
        Assert.Null(persisted.ClaimToken);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedOwner_RollsBackSimulationAndLeavesOwnership(
        bool expired)
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(token);
        context.Add(job);
        await context.SaveChangesAsync();
        await SetLeaseAsync(context, job.Id, expired);
        context.ChangeTracker.Clear();
        int simulationsBefore = await context.Simulations.CountAsync();
        int statesBefore = await context.SimulationStates.CountAsync();
        SimulationJobEntity before = await ReadJobAsync(context, job.Id);

        SimulationJobResult? result =
            await new EfSimulationJobCompletionRepository(context)
                .SaveSimulationAndCompleteAsync(
                    job.Id,
                    expired ? token : Guid.NewGuid(),
                    CreateSimulation(),
                    default);

        Assert.Null(result);
        Assert.Equal(simulationsBefore, await context.Simulations.CountAsync());
        Assert.Equal(statesBefore, await context.SimulationStates.CountAsync());
        SimulationJobEntity after = await ReadJobAsync(context, job.Id);
        Assert.Equal(SimulationJobStatus.Running, after.Status);
        Assert.Equal(before.ClaimToken, after.ClaimToken);
        Assert.Equal(before.LeaseExpiresAtUtc, after.LeaseExpiresAtUtc);
        Assert.Null(after.SimulationId);
        Assert.Null(after.FinishedAtUtc);
        Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CallerTransaction_RejectedOwnerRollsBackOnlySavepoint()
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(token);
        context.Add(job);
        await context.SaveChangesAsync();
        await SetLeaseAsync(context, job.Id, expired: false);
        SimulationEntity unrelated = CreateUnrelatedSimulation();
        context.Add(unrelated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        SimulationJobResult? result =
            await new EfSimulationJobCompletionRepository(context)
                .SaveSimulationAndCompleteAsync(
                    job.Id,
                    Guid.NewGuid(),
                    CreateSimulation(),
                    default);

        Assert.Null(result);
        Assert.True(await context.Simulations.AnyAsync(
            simulation => simulation.Id == unrelated.Id));
        Assert.Equal(SimulationJobStatus.Running,
            (await ReadJobAsync(context, job.Id)).Status);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task PreCancelledToken_LeavesNoRowsAndOwnershipUnchanged()
    {
        await using SingularFlowDbContext context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid token = Guid.NewGuid();
        SimulationJobEntity job = CreateRunningJob(token);
        context.Add(job);
        await context.SaveChangesAsync();
        await SetLeaseAsync(context, job.Id, expired: false);
        context.ChangeTracker.Clear();
        int simulationsBefore = await context.Simulations.CountAsync();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfSimulationJobCompletionRepository(context)
                .SaveSimulationAndCompleteAsync(
                    job.Id,
                    token,
                    CreateSimulation(),
                    cancellation.Token));

        Assert.Equal(simulationsBefore, await context.Simulations.CountAsync());
        SimulationJobEntity persisted = await ReadJobAsync(context, job.Id);
        Assert.Equal(SimulationJobStatus.Running, persisted.Status);
        Assert.Equal(token, persisted.ClaimToken);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ReplacedOwner_OldAttemptRollsBackAndNewOwnerCompletes()
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
        int simulationsBefore = await context.Simulations.CountAsync();
        EfSimulationJobCompletionRepository repository = new(context);

        Assert.Null(await repository.SaveSimulationAndCompleteAsync(
            job.Id, ownerA, CreateSimulation(), default));
        Assert.Equal(simulationsBefore, await context.Simulations.CountAsync());

        SimulationJobResult? result =
            await repository.SaveSimulationAndCompleteAsync(
                job.Id,
                ownerB.ClaimToken,
                CreateSimulation(),
                default);

        Assert.NotNull(result);
        Assert.Equal(simulationsBefore + 1, await context.Simulations.CountAsync());
        Assert.Equal(3, await context.SimulationStates.CountAsync(
            state => state.SimulationId == result.SimulationId));
        await transaction.RollbackAsync();
    }

    private static SingularFlowDbContext CreateContext()
    {
        string value = Environment.GetEnvironmentVariable(
            "SINGULARFLOW_TEST_CONNECTION_STRING") ??
            throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");
        NpgsqlConnectionStringBuilder builder = new(value);
        Assert.Equal("singular_flow_tests", builder.Database);
        return new SingularFlowDbContext(
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(builder.ConnectionString).Options);
    }

    private static SimulationJobEntity CreateRunningJob(Guid token)
    {
        DateTimeOffset timestamp =
            new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        return new SimulationJobEntity
        {
            Id = Guid.NewGuid(),
            SingularTime = 2.5,
            ConcentrationExponent = 0.004,
            StartTime = 0.25,
            EndTime = 1.75,
            SampleCount = 3,
            SamplingMode = SamplingMode.Uniform,
            Status = SimulationJobStatus.Running,
            CreatedAtUtc = timestamp,
            ClaimedAtUtc = timestamp,
            ClaimToken = token,
            LeaseExpiresAtUtc = timestamp.AddYears(2)
        };
    }

    private static SimulationEntity CreateUnrelatedSimulation() => new()
    {
        Id = Guid.NewGuid(),
        SingularTime = 3.0,
        ConcentrationExponent = 0.003,
        StartTime = 0.0,
        EndTime = 1.0,
        SampleCount = 2,
        SamplingMode = SamplingMode.Uniform
    };

    private static RunSimulationResult CreateSimulation() => new(
        new BlowupParameters(2.5, 0.004),
        new TimeSeriesParameters(0.25, 1.75, 3),
        SamplingMode.Uniform,
        [
            new BlowupState(0.25, 2.25, 1, 1, 1, 1, 1, 1),
            new BlowupState(1.0, 1.5, 1, 1, 1, 1, 1, 1),
            new BlowupState(1.75, 0.75, 1, 1, 1, 1, 1, 1)
        ]);

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