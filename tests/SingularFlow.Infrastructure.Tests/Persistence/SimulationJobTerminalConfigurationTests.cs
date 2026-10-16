using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class SimulationJobTerminalConfigurationTests
{
    private const string LifecycleConstraint =
        "(\"status\" = 'Pending' AND \"claimed_at_utc\" IS NULL " +
        "AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL " +
        "AND \"failure_message\" IS NULL) OR " +
        "(\"status\" = 'Running' AND \"claimed_at_utc\" IS NOT NULL " +
        "AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL " +
        "AND \"failure_message\" IS NULL) OR " +
        "(\"status\" = 'Completed' AND \"claimed_at_utc\" IS NOT NULL " +
        "AND \"finished_at_utc\" IS NOT NULL " +
        "AND \"failure_message\" IS NULL) OR " +
        "(\"status\" = 'Failed' AND \"claimed_at_utc\" IS NOT NULL " +
        "AND \"finished_at_utc\" IS NOT NULL AND \"simulation_id\" IS NULL " +
        "AND \"failure_message\" IS NOT NULL " +
        "AND \"failure_message\" ~ '[^[:space:]]') OR " +
        "(\"status\" = 'Cancelled' AND \"finished_at_utc\" IS NULL " +
        "AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL)";

    [Fact]
    public void Model_MapsTerminalColumnsWithApprovedNullabilityAndGeneration()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);
        StoreObjectIdentifier table = StoreObjectIdentifier.Table(
            entityType.GetTableName()!,
            entityType.GetSchema());

        IProperty simulationId = AssertProperty(
            entityType,
            table,
            "SimulationId",
            "simulation_id",
            "uuid",
            typeof(Guid?),
            isNullable: true);
        Assert.Equal(ValueGenerated.Never, simulationId.ValueGenerated);

        IProperty finishedAt = AssertProperty(
            entityType,
            table,
            "FinishedAtUtc",
            "finished_at_utc",
            "timestamp with time zone",
            typeof(DateTimeOffset?),
            isNullable: true);
        Assert.Equal(ValueGenerated.Never, finishedAt.ValueGenerated);
        Assert.Null(finishedAt.GetDefaultValueSql());

        IProperty failureMessage = AssertProperty(
            entityType,
            table,
            "FailureMessage",
            "failure_message",
            "character varying(1000)",
            typeof(string),
            isNullable: true);
        Assert.Equal(
            SimulationJobResult.MaximumFailureMessageLength,
            failureMessage.GetMaxLength());
    }

    [Fact]
    public void Model_MapsOptionalSimulationReferenceWithSetNullAndIndex()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);
        IForeignKey foreignKey = Assert.Single(
            entityType.GetForeignKeys());

        Assert.Equal(
            "SimulationId",
            Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(
            typeof(SimulationEntity),
            foreignKey.PrincipalEntityType.ClrType);
        Assert.False(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
        Assert.Empty(entityType.GetNavigations());
        Assert.Null(foreignKey.DependentToPrincipal);
        Assert.Null(foreignKey.PrincipalToDependent);

        IIndex index = Assert.Single(
            entityType.GetIndexes(),
            candidate => candidate.Properties
                .Select(property => property.Name)
                .SequenceEqual(
                    ["SimulationId"]));

        Assert.Equal(
            "ix_simulation_jobs_simulation_id",
            index.GetDatabaseName());
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Model_ConfiguresExactLifecycleConstraint()
    {
        using SingularFlowDbContext context = CreateContext();
        IModel designTimeModel =
            context.GetService<IDesignTimeModel>().Model;
        IEntityType entityType = GetJobEntityType(designTimeModel);
        ICheckConstraint constraint = Assert.Single(
            entityType.GetCheckConstraints(),
            candidate =>
                candidate.Name == "ck_simulation_jobs_lifecycle");

        Assert.Equal(LifecycleConstraint, constraint.Sql);
    }

    [Fact]
    public void Model_PreservesExistingSimulationJobAndStateMappings()
    {
        using SingularFlowDbContext context = CreateContext();

        Assert.Equal(
            "simulation_jobs",
            context.Model.FindEntityType(typeof(SimulationJobEntity))
                ?.GetTableName());
        Assert.Equal(
            "simulations",
            context.Model.FindEntityType(typeof(SimulationEntity))
                ?.GetTableName());
        Assert.Equal(
            "simulation_states",
            context.Model.FindEntityType(typeof(SimulationStateEntity))
                ?.GetTableName());

        IIndex[] jobIndexes = context.Model
            .FindEntityType(typeof(SimulationJobEntity))!
            .GetIndexes()
            .ToArray();

        Assert.Contains(
            jobIndexes,
            index => index.GetDatabaseName() ==
                "ix_simulation_jobs_status_created_at_utc_id");
    }

    private static IProperty AssertProperty(
        IEntityType entityType,
        StoreObjectIdentifier table,
        string propertyName,
        string columnName,
        string columnType,
        Type clrType,
        bool isNullable)
    {
        IProperty property = Assert.IsAssignableFrom<IProperty>(
            entityType.FindProperty(propertyName));

        Assert.Equal(clrType, property.ClrType);
        Assert.Equal(columnName, property.GetColumnName(table));
        Assert.Equal(columnType, property.GetColumnType());
        Assert.Equal(isNullable, property.IsNullable);

        return property;
    }

    private static IEntityType GetJobEntityType(IModel model) =>
        Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(SimulationJobEntity)));

    private static SingularFlowDbContext CreateContext()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5433;Database=singular_flow;" +
                    "Username=singular_flow")
                .Options;

        return new SingularFlowDbContext(options);
    }
}