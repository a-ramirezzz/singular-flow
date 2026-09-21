using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class SingularFlowDbContextModelTests
{
    [Fact]
    public void Model_ConfiguresGeneratedPrimaryKeysAndColumnNames()
    {
        using SingularFlowDbContext context = CreateContext();

        IModel model = context.Model;

        IEntityType simulationEntityType =
            model.FindEntityType(typeof(SimulationEntity))!;

        IEntityType stateEntityType =
            model.FindEntityType(typeof(SimulationStateEntity))!;

        Assert.NotNull(simulationEntityType);
        Assert.NotNull(stateEntityType);

        IKey simulationPrimaryKey =
            simulationEntityType.FindPrimaryKey()!;

        IKey statePrimaryKey =
            stateEntityType.FindPrimaryKey()!;

        Assert.NotNull(simulationPrimaryKey);
        Assert.NotNull(statePrimaryKey);

        IProperty simulationIdProperty =
            Assert.Single(simulationPrimaryKey.Properties);

        IProperty stateIdProperty =
            Assert.Single(statePrimaryKey.Properties);

        Assert.Equal(
            nameof(SimulationEntity.Id),
            simulationIdProperty.Name);

        Assert.Equal(
            nameof(SimulationStateEntity.Id),
            stateIdProperty.Name);

        Assert.Equal(
            ValueGenerated.OnAdd,
            simulationIdProperty.ValueGenerated);

        Assert.Equal(
            ValueGenerated.OnAdd,
            stateIdProperty.ValueGenerated);

        Assert.Equal(
            "pk_simulations",
            simulationPrimaryKey.GetName());

        Assert.Equal(
            "pk_simulation_states",
            statePrimaryKey.GetName());

        StoreObjectIdentifier simulationTable =
            StoreObjectIdentifier.Table(
                "simulations",
                schema: null);

        StoreObjectIdentifier stateTable =
            StoreObjectIdentifier.Table(
                "simulation_states",
                schema: null);

        Assert.Equal(
            "id",
            simulationIdProperty.GetColumnName(
                simulationTable));

        Assert.Equal(
            "id",
            stateIdProperty.GetColumnName(
                stateTable));

        IProperty simulationIdForeignKeyProperty =
            stateEntityType.FindProperty(
                nameof(SimulationStateEntity.SimulationId))!;

        Assert.NotNull(simulationIdForeignKeyProperty);

        Assert.Equal(
            "simulation_id",
            simulationIdForeignKeyProperty.GetColumnName(
                stateTable));
    }
    [Fact]
    public void Model_MapsSimulationAggregateToExpectedTables()
    {
        using SingularFlowDbContext context = CreateContext();

        IModel model = context.Model;

        IEntityType simulationEntityType =
            model.FindEntityType(typeof(SimulationEntity))!;

        IEntityType stateEntityType =
            model.FindEntityType(typeof(SimulationStateEntity))!;

        Assert.NotNull(simulationEntityType);
        Assert.NotNull(stateEntityType);

        Assert.Equal(
            "simulations",
            simulationEntityType.GetTableName());

        Assert.Equal(
            "simulation_states",
            stateEntityType.GetTableName());
    }

    [Fact]
    public void Model_ConfiguresRequiredOneToManyRelationship()
    {
        using SingularFlowDbContext context = CreateContext();

        IModel model = context.Model;

        IEntityType simulationEntityType =
            model.FindEntityType(typeof(SimulationEntity))!;

        IEntityType stateEntityType =
            model.FindEntityType(typeof(SimulationStateEntity))!;

        Assert.NotNull(simulationEntityType);
        Assert.NotNull(stateEntityType);

        IForeignKey foreignKey =
            Assert.Single(stateEntityType.GetForeignKeys());

        Assert.Equal(
            simulationEntityType,
            foreignKey.PrincipalEntityType);

        Assert.False(foreignKey.IsUnique);
        Assert.True(foreignKey.IsRequired);

        Assert.Equal(
            DeleteBehavior.Cascade,
            foreignKey.DeleteBehavior);

        IProperty foreignKeyProperty =
            Assert.Single(foreignKey.Properties);

        Assert.Equal(
            "SimulationId",
            foreignKeyProperty.Name);
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