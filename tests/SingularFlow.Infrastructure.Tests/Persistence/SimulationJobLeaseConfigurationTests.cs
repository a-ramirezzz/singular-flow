using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class SimulationJobLeaseConfigurationTests
{
    [Fact]
    public void Entity_DefaultsLeaseOwnershipToNull()
    {
        SimulationJobEntity entity = new();

        Assert.Null(entity.ClaimToken);
        Assert.Null(entity.LeaseExpiresAtUtc);
    }

    [Fact]
    public void Model_MapsLeaseColumnsAsNullableAndNeverGenerated()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);
        StoreObjectIdentifier table = StoreObjectIdentifier.Table(
            entityType.GetTableName()!,
            entityType.GetSchema());

        AssertLeaseProperty(
            entityType,
            table,
            nameof(SimulationJobEntity.ClaimToken),
            "claim_token",
            "uuid",
            typeof(Guid?));
        AssertLeaseProperty(
            entityType,
            table,
            nameof(SimulationJobEntity.LeaseExpiresAtUtc),
            "lease_expires_at_utc",
            "timestamp with time zone",
            typeof(DateTimeOffset?));
    }

    [Fact]
    public void Model_ConfiguresRecoveryIndexAndPreservesPendingIndex()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);

        AssertIndex(
            entityType,
            "ix_simulation_jobs_status_lease_expires_at_utc_id",
            nameof(SimulationJobEntity.Status),
            nameof(SimulationJobEntity.LeaseExpiresAtUtc),
            nameof(SimulationJobEntity.Id));
        AssertIndex(
            entityType,
            "ix_simulation_jobs_status_created_at_utc_id",
            nameof(SimulationJobEntity.Status),
            nameof(SimulationJobEntity.CreatedAtUtc),
            nameof(SimulationJobEntity.Id));
    }

    [Fact]
    public void Model_AddsNoLeaseOwnershipRelationshipOrNavigation()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);

        Assert.Single(entityType.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
    }

    private static void AssertLeaseProperty(
        IEntityType entityType,
        StoreObjectIdentifier table,
        string propertyName,
        string columnName,
        string columnType,
        Type clrType)
    {
        IProperty property = Assert.IsAssignableFrom<IProperty>(
            entityType.FindProperty(propertyName));

        Assert.Equal(clrType, property.ClrType);
        Assert.Equal(columnName, property.GetColumnName(table));
        Assert.Equal(columnType, property.GetColumnType());
        Assert.True(property.IsNullable);
        Assert.Null(property.GetDefaultValueSql());
        Assert.Null(property.GetDefaultValue());
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    private static void AssertIndex(
        IEntityType entityType,
        string databaseName,
        params string[] propertyNames)
    {
        IIndex index = Assert.Single(
            entityType.GetIndexes(),
            candidate => candidate.GetDatabaseName() == databaseName);

        Assert.Equal(
            propertyNames,
            index.Properties.Select(property => property.Name));
        Assert.False(index.IsUnique);
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