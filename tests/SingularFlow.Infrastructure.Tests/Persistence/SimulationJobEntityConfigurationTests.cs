using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class SimulationJobEntityConfigurationTests
{
    [Fact]
    public void Entity_DefaultsStatusToPending()
    {
        SimulationJobEntity entity = new();

        Assert.Equal(
            SimulationJobStatus.Pending,
            entity.Status);
    }

    [Fact]
    public void Model_MapsJobPropertiesToExpectedTableAndColumns()
    {
        using SingularFlowDbContext context = CreateContext();

        IEntityType entityType = GetEntityType(context.Model);

        Assert.Equal("simulation_jobs", entityType.GetTableName());
        Assert.Same(entityType, context.SimulationJobs.EntityType);

        IKey primaryKey = entityType.FindPrimaryKey()!;
        IProperty idProperty = Assert.Single(primaryKey.Properties);

        Assert.Equal(nameof(SimulationJobEntity.Id), idProperty.Name);
        Assert.Equal(ValueGenerated.OnAdd, idProperty.ValueGenerated);

        AssertProperty(entityType, nameof(SimulationJobEntity.Id),
            "id", "uuid", typeof(Guid));
        AssertProperty(entityType, nameof(SimulationJobEntity.SingularTime),
            "singular_time", "double precision", typeof(double));
        AssertProperty(entityType, nameof(SimulationJobEntity.ConcentrationExponent),
            "concentration_exponent", "double precision", typeof(double));
        AssertProperty(entityType, nameof(SimulationJobEntity.StartTime),
            "start_time", "double precision", typeof(double));
        AssertProperty(entityType, nameof(SimulationJobEntity.EndTime),
            "end_time", "double precision", typeof(double));
        AssertProperty(entityType, nameof(SimulationJobEntity.SampleCount),
            "sample_count", "integer", typeof(int));

        IProperty samplingMode = AssertProperty(
            entityType, nameof(SimulationJobEntity.SamplingMode),
            "sampling_mode", "character varying(32)", typeof(SamplingMode));
        AssertStringEnum(samplingMode);

        IProperty status = AssertProperty(
            entityType, nameof(SimulationJobEntity.Status),
            "status", "character varying(32)", typeof(SimulationJobStatus));
        AssertStringEnum(status);

        IProperty createdAt = AssertProperty(
            entityType, nameof(SimulationJobEntity.CreatedAtUtc),
            "created_at_utc", "timestamp with time zone",
            typeof(DateTimeOffset));
        Assert.Equal(ValueGenerated.OnAdd, createdAt.ValueGenerated);
        Assert.Equal("CURRENT_TIMESTAMP", createdAt.GetDefaultValueSql());
    }

    [Fact]
    public void Model_ConfiguresJobCheckConstraints()
    {
        using SingularFlowDbContext context = CreateContext();

        IModel designTimeModel =
            context.GetService<IDesignTimeModel>().Model;
        IEntityType entityType = GetEntityType(designTimeModel);
        IReadOnlyList<ICheckConstraint> constraints =
            entityType.GetCheckConstraints().ToArray();

        Assert.Equal(6, constraints.Count);
        AssertConstraint(constraints, "ck_simulation_jobs_sample_count",
            "\"sample_count\" >= 2 AND \"sample_count\" <= 100000");
        AssertConstraint(constraints,
            "ck_simulation_jobs_concentration_exponent",
            "\"concentration_exponent\" > 0 AND \"concentration_exponent\" < 0.01");
        AssertConstraint(constraints, "ck_simulation_jobs_start_time",
            "\"start_time\" >= 0");
        AssertConstraint(constraints, "ck_simulation_jobs_end_time",
            "\"end_time\" > \"start_time\"");
        AssertConstraint(constraints, "ck_simulation_jobs_singular_time",
            "\"singular_time\" > \"end_time\"");

        AssertConstraint(constraints, "ck_simulation_jobs_status",
            "\"status\" IN ('Pending', 'Running', 'Completed', 'Failed', 'Cancelled')");
    }

    [Fact]
    public void Model_ConfiguresPendingSelectionIndexWithoutRelationship()
    {
        using SingularFlowDbContext context = CreateContext();

        IEntityType entityType = GetEntityType(context.Model);
        IIndex index = Assert.Single(entityType.GetIndexes());

        Assert.Equal(
            new[]
            {
                nameof(SimulationJobEntity.Status),
                nameof(SimulationJobEntity.CreatedAtUtc),
                nameof(SimulationJobEntity.Id)
            },
            index.Properties.Select(property => property.Name));
        Assert.Equal(
            "ix_simulation_jobs_status_created_at_utc_id",
            index.GetDatabaseName());
        Assert.False(index.IsUnique);
        Assert.Empty(entityType.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
    }

    [Fact]
    public void Model_RetainsExistingSimulationAggregateMappings()
    {
        using SingularFlowDbContext context = CreateContext();

        Assert.Equal(
            "simulations",
            context.Model.FindEntityType(typeof(SimulationEntity))?.GetTableName());
        Assert.Equal(
            "simulation_states",
            context.Model.FindEntityType(typeof(SimulationStateEntity))?.GetTableName());
    }

    private static IEntityType GetEntityType(IModel model) =>
        Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(SimulationJobEntity)));

    private static IProperty AssertProperty(
        IEntityType entityType,
        string propertyName,
        string columnName,
        string columnType,
        Type clrType)
    {
        IProperty property = Assert.IsAssignableFrom<IProperty>(
            entityType.FindProperty(propertyName));
        StoreObjectIdentifier table = StoreObjectIdentifier.Table(
            entityType.GetTableName()!, entityType.GetSchema());

        Assert.Equal(clrType, property.ClrType);
        Assert.Equal(columnName, property.GetColumnName(table));
        Assert.Equal(columnType, property.GetColumnType());
        Assert.False(property.IsNullable);

        return property;
    }

    private static void AssertStringEnum(IProperty property)
    {
        Assert.Equal(32, property.GetMaxLength());
        Assert.Equal(
            typeof(string),
            property.GetTypeMapping().Converter?.ProviderClrType);
    }

    private static void AssertConstraint(
        IEnumerable<ICheckConstraint> constraints,
        string name,
        string sql)
    {
        ICheckConstraint constraint = Assert.Single(
            constraints,
            candidate => candidate.Name == name);

        Assert.Equal(sql, constraint.Sql);
    }

    private static SingularFlowDbContext CreateContext()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5433;Database=singular_flow;" +
                    "Username=singular_flow;Password=test")
                .Options;

        return new SingularFlowDbContext(options);
    }
}