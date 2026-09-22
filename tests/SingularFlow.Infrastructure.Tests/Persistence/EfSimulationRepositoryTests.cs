using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationRepositoryTests
{
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

        await repository.SaveAsync(
            result,
            CancellationToken.None);

        Guid simulationId =
            Assert.Single(
                context.ChangeTracker
                    .Entries<SimulationEntity>())
                .Entity.Id;

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

        await transaction.RollbackAsync();
    }
}