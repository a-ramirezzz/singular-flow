using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SingularFlow.Infrastructure.Persistence.DesignTime;

public sealed class SingularFlowDbContextFactory :
    IDesignTimeDbContextFactory<SingularFlowDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;" +
        "Port=5432;" +
        "Database=singular_flow;" +
        "Username=singular_flow";

    public SingularFlowDbContext CreateDbContext(
        string[] args)
    {
        DbContextOptionsBuilder<SingularFlowDbContext>
            optionsBuilder = new();

        optionsBuilder.UseNpgsql(
            DesignTimeConnectionString);

        return new SingularFlowDbContext(
            optionsBuilder.Options);
    }
}