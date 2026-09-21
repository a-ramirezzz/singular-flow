using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class SimulationEntityConfigurationTests
{
    [Fact]
    public void Model_ConfiguresCreatedAtUtcAsDatabaseGenerated()
    {
        using SingularFlowDbContext context = CreateContext();

        IEntityType simulationEntityType =
            Assert.IsAssignableFrom<IEntityType>(
                context.Model.FindEntityType(
                    typeof(SimulationEntity)));

        IProperty createdAtProperty =
            Assert.IsAssignableFrom<IProperty>(
                simulationEntityType.FindProperty(
                    nameof(SimulationEntity.CreatedAtUtc)));

        Assert.Equal(
            ValueGenerated.OnAdd,
            createdAtProperty.ValueGenerated);

        Assert.Equal(
            "CURRENT_TIMESTAMP",
            createdAtProperty.GetDefaultValueSql());
    }

    [Fact]
    public void Model_MapsSimulationPropertiesToExpectedColumns()
    {
        using SingularFlowDbContext context = CreateContext();

        IEntityType entityType =
            context.Model.FindEntityType(
                typeof(SimulationEntity))!;

        Assert.NotNull(entityType);

        AssertProperty(
            entityType,
            propertyName: "SingularTime",
            columnName: "singular_time",
            columnType: "double precision",
            clrType: typeof(double));

        AssertProperty(
            entityType,
            propertyName: "ConcentrationExponent",
            columnName: "concentration_exponent",
            columnType: "double precision",
            clrType: typeof(double));

        AssertProperty(
            entityType,
            propertyName: "StartTime",
            columnName: "start_time",
            columnType: "double precision",
            clrType: typeof(double));

        AssertProperty(
            entityType,
            propertyName: "EndTime",
            columnName: "end_time",
            columnType: "double precision",
            clrType: typeof(double));

        AssertProperty(
            entityType,
            propertyName: "SampleCount",
            columnName: "sample_count",
            columnType: "integer",
            clrType: typeof(int));

        IProperty samplingModeProperty = AssertProperty(
            entityType,
            propertyName: "SamplingMode",
            columnName: "sampling_mode",
            columnType: "character varying(32)",
            clrType: typeof(SamplingMode));

        Assert.Equal(
            32,
            samplingModeProperty.GetMaxLength());

        Assert.Equal(
            typeof(string),
            samplingModeProperty
                .GetTypeMapping()
                .Converter?
                .ProviderClrType);

        AssertProperty(
            entityType,
            propertyName: "CreatedAtUtc",
            columnName: "created_at_utc",
            columnType: "timestamp with time zone",
            clrType: typeof(DateTimeOffset));
    }

    private static IProperty AssertProperty(
        IEntityType entityType,
        string propertyName,
        string columnName,
        string columnType,
        Type clrType)
    {
        IProperty property =
            Assert.IsAssignableFrom<IProperty>(
                entityType.FindProperty(propertyName));

        StoreObjectIdentifier table =
            StoreObjectIdentifier.Table(
                entityType.GetTableName()!,
                entityType.GetSchema());

        Assert.Equal(
            clrType,
            property.ClrType);

        Assert.Equal(
            columnName,
            property.GetColumnName(table));

        Assert.Equal(
            columnType,
            property.GetColumnType());

        return property;
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