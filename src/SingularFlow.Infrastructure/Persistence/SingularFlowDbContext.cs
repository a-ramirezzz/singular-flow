using Microsoft.EntityFrameworkCore;

using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class SingularFlowDbContext : DbContext
{
    public SingularFlowDbContext(
        DbContextOptions<SingularFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<SimulationEntity> Simulations =>
        Set<SimulationEntity>();

    public DbSet<SimulationStateEntity> SimulationStates =>
        Set<SimulationStateEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(SingularFlowDbContext).Assembly);
    }
}