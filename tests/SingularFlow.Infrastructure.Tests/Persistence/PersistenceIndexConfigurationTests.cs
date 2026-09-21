using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class PersistenceIndexConfigurationTests
{
    [Fact]
    public void Model_ConfiguresSimulationCreatedAtIndex()
    {
        using SingularFlowDbContext context = CreateContext();

        IEntityType entityType =
            context.Model.FindEntityType(
                typeof(SimulationEntity))!;

        Assert.NotNull(entityType);

        IIndex index = Assert.Single(entityType
                .GetIndexes()
, candidate =>
                    candidate.Properties.Count == 1 &&
                    candidate.Properties[0].Name ==
                    nameof(SimulationEntity.CreatedAtUtc));

        Assert.Equal(
            "ix_simulations_created_at_utc",
            index.GetDatabaseName());

        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Model_ConfiguresUniqueSimulationStateSequenceIndex()
    {
        using SingularFlowDbContext context = CreateContext();

        IEntityType entityType =
            context.Model.FindEntityType(
                typeof(SimulationStateEntity))!;

        Assert.NotNull(entityType);

        IIndex index = Assert.Single(entityType
                .GetIndexes()
, candidate =>
                    candidate.Properties.Count == 2 &&
                    candidate.Properties[0].Name ==
                    nameof(SimulationStateEntity.SimulationId) &&
                    candidate.Properties[1].Name ==
                    nameof(SimulationStateEntity.Sequence));

        Assert.Equal(
            "ux_simulation_states_simulation_id_sequence",
            index.GetDatabaseName());

        Assert.True(index.IsUnique);
    }

    private static SingularFlowDbContext CreateContext()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(
                    "Host=localhost;" +
                    "Port=5433;" +
                    "Database=singular_flow;" +
                    "Username=singular_flow;" +
                    "Password=test")
                .Options;

        return new SingularFlowDbContext(options);
    }
}