using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence.Configurations;

internal sealed class SimulationEntityConfiguration :
    IEntityTypeConfiguration<SimulationEntity>
{
    public void Configure(
        EntityTypeBuilder<SimulationEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
         "simulations",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_simulations_sample_count",
                    "\"sample_count\" >= 2 AND " +
                    "\"sample_count\" <= 100000");

                tableBuilder.HasCheckConstraint(
                    "ck_simulations_concentration_exponent",
                    "\"concentration_exponent\" > 0 AND " +
                    "\"concentration_exponent\" < 0.01");

                tableBuilder.HasCheckConstraint(
                    "ck_simulations_start_time",
                    "\"start_time\" >= 0");

                tableBuilder.HasCheckConstraint(
                    "ck_simulations_end_time",
                    "\"end_time\" > \"start_time\"");

                tableBuilder.HasCheckConstraint(
                    "ck_simulations_singular_time",
                    "\"singular_time\" > \"end_time\"");
            });

        builder
            .HasKey(simulation => simulation.Id)
            .HasName("pk_simulations");

        builder
            .Property(simulation => simulation.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder
            .Property(simulation => simulation.SingularTime)
            .HasColumnName("singular_time")
            .HasColumnType("double precision");

        builder
            .Property(
                simulation =>
                    simulation.ConcentrationExponent)
            .HasColumnName("concentration_exponent")
            .HasColumnType("double precision");

        builder
            .Property(simulation => simulation.StartTime)
            .HasColumnName("start_time")
            .HasColumnType("double precision");

        builder
            .Property(simulation => simulation.EndTime)
            .HasColumnName("end_time")
            .HasColumnType("double precision");

        builder
            .Property(simulation => simulation.SampleCount)
            .HasColumnName("sample_count")
            .HasColumnType("integer");

        builder
            .Property(simulation => simulation.SamplingMode)
            .HasColumnName("sampling_mode")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnType("character varying(32)");

        builder
            .Property(simulation => simulation.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder
            .HasIndex(simulation => simulation.CreatedAtUtc)
            .HasDatabaseName(
            "ix_simulations_created_at_utc");
    }
}