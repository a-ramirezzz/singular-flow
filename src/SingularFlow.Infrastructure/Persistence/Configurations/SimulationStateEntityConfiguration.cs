using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence.Configurations;

internal sealed class SimulationStateEntityConfiguration :
    IEntityTypeConfiguration<SimulationStateEntity>
{
    public void Configure(
        EntityTypeBuilder<SimulationStateEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "simulation_states",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_simulation_states_sequence",
                    "\"sequence\" >= 0");
            });

        builder
            .HasKey(state => state.Id)
            .HasName("pk_simulation_states");

        builder
            .Property(state => state.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder
            .Property(state => state.SimulationId)
            .HasColumnName("simulation_id");

        builder
            .Property(state => state.Sequence)
            .HasColumnName("sequence")
            .HasColumnType("integer");

        builder
            .Property(state => state.Time)
            .HasColumnName("time")
            .HasColumnType("double precision");

        builder
            .Property(state => state.RemainingTime)
            .HasColumnName("remaining_time")
            .HasColumnType("double precision");

        builder
            .Property(state => state.RadialLength)
            .HasColumnName("radial_length")
            .HasColumnType("double precision");

        builder
            .Property(state => state.AxialLength)
            .HasColumnName("axial_length")
            .HasColumnType("double precision");

        builder
            .Property(state => state.AngularVelocityScale)
            .HasColumnName("angular_velocity_scale")
            .HasColumnType("double precision");

        builder
            .Property(state => state.RadialVelocityScale)
            .HasColumnName("radial_velocity_scale")
            .HasColumnType("double precision");

        builder
            .Property(state => state.CoreVolumeScale)
            .HasColumnName("core_volume_scale")
            .HasColumnType("double precision");

        builder
            .Property(state => state.CoreEnergyScale)
            .HasColumnName("core_energy_scale")
            .HasColumnType("double precision");

        builder
             .HasIndex(
                state => new
                {
                    state.SimulationId,
                    state.Sequence
                })
            .IsUnique()
            .HasDatabaseName(
                "ux_simulation_states_simulation_id_sequence");

        builder
            .HasOne(state => state.Simulation)
            .WithMany(simulation => simulation.States)
            .HasForeignKey(state => state.SimulationId)
            .HasConstraintName(
                "fk_simulation_states_simulations_simulation_id")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}