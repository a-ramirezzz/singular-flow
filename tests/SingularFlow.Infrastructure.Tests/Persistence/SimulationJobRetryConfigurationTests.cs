using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class SimulationJobRetryConfigurationTests
{
    private const string LifecycleConstraint =
        "(\"status\" = 'Pending' AND \"claimed_at_utc\" IS NULL " +
        "AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL " +
        "AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL " +
        "AND \"failure_message\" IS NULL AND \"next_attempt_at_utc\" IS NOT NULL " +
        "AND \"attempt_count\" < \"max_attempts\") OR " +
        "(\"status\" = 'Running' AND \"claimed_at_utc\" IS NOT NULL " +
        "AND \"claim_token\" IS NOT NULL AND \"lease_expires_at_utc\" IS NOT NULL " +
        "AND \"lease_expires_at_utc\" > \"claimed_at_utc\" " +
        "AND \"next_attempt_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL " +
        "AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL " +
        "AND \"attempt_count\" >= 1 AND \"attempt_count\" <= \"max_attempts\") OR " +
        "(\"status\" = 'Completed' AND \"claimed_at_utc\" IS NOT NULL " +
        "AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL " +
        "AND \"next_attempt_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL " +
        "AND \"failure_message\" IS NULL AND \"attempt_count\" >= 1 " +
        "AND \"attempt_count\" <= \"max_attempts\") OR " +
        "(\"status\" = 'Failed' AND \"claimed_at_utc\" IS NOT NULL " +
        "AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL " +
        "AND \"next_attempt_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL " +
        "AND \"simulation_id\" IS NULL AND \"failure_message\" IS NOT NULL " +
        "AND \"failure_message\" ~ '[^[:space:]]' AND \"attempt_count\" >= 1 " +
        "AND \"attempt_count\" <= \"max_attempts\") OR " +
        "(\"status\" = 'Cancelled' AND \"claim_token\" IS NULL " +
        "AND \"lease_expires_at_utc\" IS NULL AND \"next_attempt_at_utc\" IS NULL " +
        "AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL " +
        "AND \"failure_message\" IS NULL AND \"attempt_count\" >= 0 " +
        "AND \"attempt_count\" <= \"max_attempts\")";

    [Fact]
    public void Entity_DefaultsRetryMetadataToClrDefaults()
    {
        SimulationJobEntity entity = new();

        Assert.Equal(0, entity.AttemptCount);
        Assert.Equal(0, entity.MaxAttempts);
        Assert.Null(entity.NextAttemptAtUtc);
        Assert.Null(entity.LastFailedAtUtc);
        Assert.Null(entity.LastFailureMessage);
    }

    [Fact]
    public void Model_MapsRetryColumnsWithExactMetadata()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);
        StoreObjectIdentifier table = StoreObjectIdentifier.Table(
            entityType.GetTableName()!, entityType.GetSchema());

        IProperty attemptCount = AssertProperty(
            entityType, table, nameof(SimulationJobEntity.AttemptCount),
            "attempt_count", "integer", typeof(int), isNullable: false);
        Assert.Equal(0, attemptCount.GetDefaultValue());
        Assert.Null(attemptCount.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.OnAdd, attemptCount.ValueGenerated);
        Assert.Null(attemptCount.GetTypeMapping().Converter);

        IProperty maxAttempts = AssertProperty(
            entityType, table, nameof(SimulationJobEntity.MaxAttempts),
            "max_attempts", "integer", typeof(int), isNullable: false);
        Assert.Equal(3, maxAttempts.GetDefaultValue());
        Assert.Null(maxAttempts.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.OnAdd, maxAttempts.ValueGenerated);
        Assert.Null(maxAttempts.GetTypeMapping().Converter);

        IProperty nextAttemptAt = AssertProperty(
            entityType, table, nameof(SimulationJobEntity.NextAttemptAtUtc),
            "next_attempt_at_utc", "timestamp with time zone",
            typeof(DateTimeOffset?), isNullable: true);
        Assert.Null(nextAttemptAt.GetDefaultValue());
        Assert.Equal("CURRENT_TIMESTAMP", nextAttemptAt.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.OnAdd, nextAttemptAt.ValueGenerated);
        Assert.Null(nextAttemptAt.GetTypeMapping().Converter);

        IProperty lastFailedAt = AssertProperty(
            entityType, table, nameof(SimulationJobEntity.LastFailedAtUtc),
            "last_failed_at_utc", "timestamp with time zone",
            typeof(DateTimeOffset?), isNullable: true);
        Assert.Null(lastFailedAt.GetDefaultValue());
        Assert.Null(lastFailedAt.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.Never, lastFailedAt.ValueGenerated);

        IProperty lastFailureMessage = AssertProperty(
            entityType, table, nameof(SimulationJobEntity.LastFailureMessage),
            "last_failure_message", "character varying(1000)",
            typeof(string), isNullable: true);
        Assert.Equal(1000, lastFailureMessage.GetMaxLength());
        Assert.Null(lastFailureMessage.GetDefaultValue());
        Assert.Null(lastFailureMessage.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.Never, lastFailureMessage.ValueGenerated);
    }

    [Fact]
    public void Model_ConfiguresExactRetryConstraints()
    {
        using SingularFlowDbContext context = CreateContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType entityType = GetJobEntityType(model);

        AssertConstraint(
            entityType,
            "ck_simulation_jobs_retry_counters",
            "\"attempt_count\" >= 0 AND \"max_attempts\" BETWEEN 1 AND 10 " +
            "AND \"attempt_count\" <= \"max_attempts\"");
        AssertConstraint(
            entityType,
            "ck_simulation_jobs_last_failure",
            "(\"last_failed_at_utc\" IS NULL AND \"last_failure_message\" IS NULL) OR " +
            "(\"last_failed_at_utc\" IS NOT NULL AND \"last_failure_message\" IS NOT NULL " +
            "AND \"last_failure_message\" ~ '[^[:space:]]')");
        AssertConstraint(
            entityType,
            "ck_simulation_jobs_lifecycle",
            LifecycleConstraint);
    }

    [Fact]
    public void Model_ReplacesPendingIndexAndPreservesExistingIndexes()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);

        AssertIndex(
            entityType,
            "ix_simulation_jobs_status_next_attempt_at_utc_created_at_utc_id",
            nameof(SimulationJobEntity.Status),
            nameof(SimulationJobEntity.NextAttemptAtUtc),
            nameof(SimulationJobEntity.CreatedAtUtc),
            nameof(SimulationJobEntity.Id));
        Assert.DoesNotContain(
            entityType.GetIndexes(),
            index => index.GetDatabaseName() ==
                "ix_simulation_jobs_status_created_at_utc_id");
        AssertIndex(
            entityType,
            "ix_simulation_jobs_status_lease_expires_at_utc_id",
            nameof(SimulationJobEntity.Status),
            nameof(SimulationJobEntity.LeaseExpiresAtUtc),
            nameof(SimulationJobEntity.Id));
        AssertIndex(
            entityType,
            "ix_simulation_jobs_simulation_id",
            nameof(SimulationJobEntity.SimulationId));
    }

    [Fact]
    public void Model_AddsNoRetryRelationshipOrNavigation()
    {
        using SingularFlowDbContext context = CreateContext();
        IEntityType entityType = GetJobEntityType(context.Model);

        Assert.Single(entityType.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
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

    private static void AssertConstraint(
        IEntityType entityType,
        string name,
        string sql)
    {
        ICheckConstraint constraint = Assert.Single(
            entityType.GetCheckConstraints(),
            candidate => candidate.Name == name);

        Assert.Equal(sql, constraint.Sql);
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