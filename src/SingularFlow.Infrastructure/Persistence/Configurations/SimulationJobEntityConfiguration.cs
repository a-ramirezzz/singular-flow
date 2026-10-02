using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence.Configurations;

internal sealed class SimulationJobEntityConfiguration :
    IEntityTypeConfiguration<SimulationJobEntity>
{
    public void Configure(
        EntityTypeBuilder<SimulationJobEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "simulation_jobs",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_sample_count",
                    "\"sample_count\" >= 2 AND " +
                    "\"sample_count\" <= 100000");

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_concentration_exponent",
                    "\"concentration_exponent\" > 0 AND " +
                    "\"concentration_exponent\" < 0.01");

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_start_time",
                    "\"start_time\" >= 0");

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_end_time",
                    "\"end_time\" > \"start_time\"");

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_singular_time",
                    "\"singular_time\" > \"end_time\"");

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_status",
                    "\"status\" IN ('Pending', 'Running', " +
                    "'Completed', 'Failed', 'Cancelled')");
            });

        builder
            .HasKey(job => job.Id)
            .HasName("pk_simulation_jobs");

        builder
            .Property(job => job.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd();

        builder
            .Property(job => job.SingularTime)
            .HasColumnName("singular_time")
            .HasColumnType("double precision");

        builder
            .Property(job => job.ConcentrationExponent)
            .HasColumnName("concentration_exponent")
            .HasColumnType("double precision");

        builder
            .Property(job => job.StartTime)
            .HasColumnName("start_time")
            .HasColumnType("double precision");

        builder
            .Property(job => job.EndTime)
            .HasColumnName("end_time")
            .HasColumnType("double precision");

        builder
            .Property(job => job.SampleCount)
            .HasColumnName("sample_count")
            .HasColumnType("integer");

        builder
            .Property(job => job.SamplingMode)
            .HasColumnName("sampling_mode")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnType("character varying(32)")
            .IsRequired();

        builder
            .Property(job => job.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnType("character varying(32)")
            .IsRequired();

        builder
            .Property(job => job.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder
            .HasIndex(job => new
            {
                job.Status,
                job.CreatedAtUtc,
                job.Id
            })
            .HasDatabaseName(
                "ix_simulation_jobs_status_created_at_utc_id");
    }
}