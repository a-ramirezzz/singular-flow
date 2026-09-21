using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.DesignTime;

namespace SingularFlow.Infrastructure.Tests.Persistence.DesignTime;

public sealed class SingularFlowDbContextFactoryTests
{
    [Fact]
    public void CreateDbContext_ConfiguresNpgsqlProvider()
    {
        SingularFlowDbContextFactory factory = new();

        using SingularFlowDbContext context =
            factory.CreateDbContext(
                Array.Empty<string>());

        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            context.Database.ProviderName);
    }
}