using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class PersistenceConstraintConfigurationTests
{
    [Fact]
    public void Model_ConfiguresSimulationCheckConstraints()
    {
        using SingularFlowDbContext context = CreateContext();

        IModel designTimeModel =
            context
                .GetService<IDesignTimeModel>()
                .Model;

        IEntityType entityType =
            designTimeModel.FindEntityType(
                typeof(SimulationEntity))!;

        Assert.NotNull(entityType);

        IReadOnlyList<ICheckConstraint> constraints =
            entityType
                .GetCheckConstraints()
                .ToArray();

        Assert.Equal(
            5,
            constraints.Count);

        AssertConstraint(
            constraints,
            name: "ck_simulations_sample_count",
            sql:
                "\"sample_count\" >= 2 AND " +
                "\"sample_count\" <= 100000");

        AssertConstraint(
            constraints,
            name:
                "ck_simulations_concentration_exponent",
            sql:
                "\"concentration_exponent\" > 0 AND " +
                "\"concentration_exponent\" < 0.01");

        AssertConstraint(
            constraints,
            name: "ck_simulations_start_time",
            sql: "\"start_time\" >= 0");

        AssertConstraint(
            constraints,
            name: "ck_simulations_end_time",
            sql: "\"end_time\" > \"start_time\"");

        AssertConstraint(
            constraints,
            name: "ck_simulations_singular_time",
            sql: "\"singular_time\" > \"end_time\"");
    }

    [Fact]
    public void Model_ConfiguresStateSequenceCheckConstraint()
    {
        using SingularFlowDbContext context = CreateContext();

        IModel designTimeModel =
            context
                .GetService<IDesignTimeModel>()
                .Model;

        IEntityType entityType =
            designTimeModel.FindEntityType(
                typeof(SimulationStateEntity))!;

        Assert.NotNull(entityType);

        IReadOnlyList<ICheckConstraint> constraints =
            entityType
                .GetCheckConstraints()
                .ToArray();

        ICheckConstraint constraint =
            Assert.Single(constraints);

        Assert.Equal(
            "ck_simulation_states_sequence",
            constraint.Name);

        Assert.Equal(
            "\"sequence\" >= 0",
            constraint.Sql);
    }

    private static void AssertConstraint(
        IEnumerable<ICheckConstraint> constraints,
        string name,
        string sql)
    {
        ICheckConstraint constraint =
            Assert.Single(constraints, candidate =>
                        candidate.Name == name);

        Assert.Equal(
            sql,
            constraint.Sql);
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