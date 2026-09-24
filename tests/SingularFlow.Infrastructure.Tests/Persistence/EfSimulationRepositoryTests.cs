using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationRepositoryTests
{
    [Fact]
    public async Task DeleteAsync_ExistingId_RemovesSimulationAndStates()
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

        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        RunSimulationResult simulation =
            new RunSimulationHandler().Handle(
                request);

        EfSimulationRepository repository = new(
            context);

        PersistedSimulationResult persisted =
            await repository.SaveAsync(
                simulation,
                CancellationToken.None);

        int stateCount =
            await context.SimulationStates
                .CountAsync(
                    state =>
                        state.SimulationId ==
                        persisted.Id);

        Assert.Equal(3, stateCount);

        bool deleted =
            await repository.DeleteAsync(
                persisted.Id,
                CancellationToken.None);

        Assert.True(deleted);

        context.ChangeTracker.Clear();

        Assert.False(
            await context.Simulations
                .AnyAsync(
                    entity =>
                        entity.Id == persisted.Id));

        Assert.False(
            await context.SimulationStates
                .AnyAsync(
                    state =>
                        state.SimulationId ==
                        persisted.Id));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DeleteAsync_MissingId_ReturnsFalse()
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

        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        EfSimulationRepository repository = new(
            context);

        bool deleted =
            await repository.DeleteAsync(
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task SaveAsync_PersistsSimulationAndOrderedStates()
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

        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        RunSimulationResult result =
            new RunSimulationHandler().Handle(request);

        EfSimulationRepository repository = new(context);

        PersistedSimulationResult persisted =
            await repository.SaveAsync(
                result,
                CancellationToken.None);

        Assert.NotEqual(
            Guid.Empty,
            persisted.Id);

        Assert.NotEqual(
            default,
            persisted.CreatedAtUtc);

        Assert.Same(
            result,
            persisted.Simulation);

        Guid simulationId = persisted.Id;

        context.ChangeTracker.Clear();

        SimulationEntity saved =
            await context.Simulations
                .AsNoTracking()
                .Include(simulation => simulation.States)
                .SingleAsync(
                    simulation =>
                        simulation.Id == simulationId);

        Assert.Equal(1.0, saved.SingularTime);
        Assert.Equal(0.005, saved.ConcentrationExponent);
        Assert.Equal(3, saved.SampleCount);
        Assert.Equal(SamplingMode.Uniform, saved.SamplingMode);
        Assert.NotEqual(default, saved.CreatedAtUtc);
        Assert.Equal(
    persisted.CreatedAtUtc,
    saved.CreatedAtUtc);

        SimulationStateEntity[] states =
            saved.States
                .OrderBy(state => state.Sequence)
                .ToArray();

        Assert.Equal(result.States.Count, states.Length);

        for (int index = 0; index < states.Length; index++)
        {
            Assert.Equal(index, states[index].Sequence);
            Assert.Equal(result.States[index].Time, states[index].Time);
            Assert.Equal(
                result.States[index].RemainingTime,
                states[index].RemainingTime);
            Assert.Equal(
                result.States[index].CoreEnergyScale,
                states[index].CoreEnergyScale);
        }

        PersistedSimulationResult? retrieved =
    await repository.GetByIdAsync(
        persisted.Id,
        CancellationToken.None);

        Assert.NotNull(retrieved);

        Assert.Equal(
            persisted.Id,
            retrieved.Id);

        Assert.Equal(
            persisted.CreatedAtUtc,
            retrieved.CreatedAtUtc);

        Assert.Equal(
            result.BlowupParameters,
            retrieved.Simulation.BlowupParameters);

        Assert.Equal(
            result.TimeSeriesParameters,
            retrieved.Simulation.TimeSeriesParameters);

        Assert.Equal(
            result.SamplingMode,
            retrieved.Simulation.SamplingMode);

        Assert.Equal(
            result.States.ToArray(),
            retrieved.Simulation.States.ToArray());

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNull()
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

        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        EfSimulationRepository repository = new(
            context);

        PersistedSimulationResult? result =
            await repository.GetByIdAsync(
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_ReturnsStablePagedSummaries()
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

        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        int baselineCount =
            await context.Simulations.CountAsync();

        SimulationEntity newest = CreateSimulation(
            id: Guid.Parse(
                "00000000-0000-0000-0000-000000000003"),
            createdAtUtc: new DateTimeOffset(
                2099,
                1,
                3,
                0,
                0,
                0,
                TimeSpan.Zero),
            singularTime: 1.3);

        SimulationEntity middle = CreateSimulation(
            id: Guid.Parse(
                "00000000-0000-0000-0000-000000000002"),
            createdAtUtc: new DateTimeOffset(
                2099,
                1,
                2,
                0,
                0,
                0,
                TimeSpan.Zero),
            singularTime: 1.2);

        SimulationEntity oldest = CreateSimulation(
            id: Guid.Parse(
                "00000000-0000-0000-0000-000000000001"),
            createdAtUtc: new DateTimeOffset(
                2099,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero),
            singularTime: 1.1);

        context.Simulations.AddRange(
            newest,
            middle,
            oldest);

        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        EfSimulationRepository repository = new(
            context);

        PagedSimulationResult firstPage =
            await repository.ListAsync(
                page: 1,
                pageSize: 2,
                CancellationToken.None);

        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(
            baselineCount + 3,
            firstPage.TotalCount);

        Assert.Equal(
            (int)Math.Ceiling(
                (baselineCount + 3) / 2.0),
            firstPage.TotalPages);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(newest.Id, firstPage.Items[0].Id);
        Assert.Equal(middle.Id, firstPage.Items[1].Id);
        Assert.Equal(1.3, firstPage.Items[0].SingularTime);
        Assert.Equal(3, firstPage.Items[0].SampleCount);

        PagedSimulationResult secondPage =
            await repository.ListAsync(
                page: 2,
                pageSize: 2,
                CancellationToken.None);

        Assert.NotEmpty(secondPage.Items);
        Assert.Equal(oldest.Id, secondPage.Items[0].Id);

        PagedSimulationResult outsideRange =
            await repository.ListAsync(
                page: baselineCount + 4,
                pageSize: 1,
                CancellationToken.None);

        Assert.Empty(outsideRange.Items);
        Assert.Equal(
            baselineCount + 3,
            outsideRange.TotalCount);

        await transaction.RollbackAsync();

        static SimulationEntity CreateSimulation(
            Guid id,
            DateTimeOffset createdAtUtc,
            double singularTime)
        {
            return new SimulationEntity
            {
                Id = id,
                SingularTime = singularTime,
                ConcentrationExponent = 0.005,
                StartTime = 0.0,
                EndTime = 0.8,
                SampleCount = 3,
                SamplingMode = SamplingMode.Uniform,
                CreatedAtUtc = createdAtUtc
            };
        }
    }
}