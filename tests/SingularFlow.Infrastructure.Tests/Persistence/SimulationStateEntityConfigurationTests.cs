using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class SimulationStateEntityConfigurationTests
{
    [Fact]
    public void Model_MapsStatePropertiesToExpectedColumns()
    {
        using SingularFlowDbContext context = CreateContext();

        IEntityType entityType =
            context.Model.FindEntityType(
                typeof(SimulationStateEntity))!;

        Assert.NotNull(entityType);

        AssertProperty(
            entityType,
            propertyName: "Sequence",
            columnName: "sequence",
            columnType: "integer",
            clrType: typeof(int));

        AssertDoubleProperty(
            entityType,
            propertyName: "Time",
            columnName: "time");

        AssertDoubleProperty(
            entityType,
            propertyName: "RemainingTime",
            columnName: "remaining_time");

        AssertDoubleProperty(
            entityType,
            propertyName: "RadialLength",
            columnName: "radial_length");

        AssertDoubleProperty(
            entityType,
            propertyName: "AxialLength",
            columnName: "axial_length");

        AssertDoubleProperty(
            entityType,
            propertyName: "AngularVelocityScale",
            columnName: "angular_velocity_scale");

        AssertDoubleProperty(
            entityType,
            propertyName: "RadialVelocityScale",
            columnName: "radial_velocity_scale");

        AssertDoubleProperty(
            entityType,
            propertyName: "CoreVolumeScale",
            columnName: "core_volume_scale");

        AssertDoubleProperty(
            entityType,
            propertyName: "CoreEnergyScale",
            columnName: "core_energy_scale");
    }

    private static void AssertDoubleProperty(
        IEntityType entityType,
        string propertyName,
        string columnName)
    {
        AssertProperty(
            entityType,
            propertyName,
            columnName,
            columnType: "double precision",
            clrType: typeof(double));
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