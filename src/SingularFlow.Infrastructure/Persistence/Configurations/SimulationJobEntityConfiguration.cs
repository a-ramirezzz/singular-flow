using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SingularFlow.Application.SimulationJobs;
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

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_lifecycle",
                    "(\"status\" = 'Pending' AND " +
                    "\"claimed_at_utc\" IS NULL AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NULL) OR " +
                    "(\"status\" = 'Running' AND " +
                    "\"claimed_at_utc\" IS NOT NULL AND " +
                    "\"claim_token\" IS NOT NULL AND " +
                    "\"lease_expires_at_utc\" IS NOT NULL AND " +
                    "\"lease_expires_at_utc\" > \"claimed_at_utc\" AND " +
                    "\"finished_at_utc\" IS NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NULL) OR " +
                    "(\"status\" = 'Completed' AND " +
                    "\"claimed_at_utc\" IS NOT NULL AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NOT NULL AND " +
                    "\"failure_message\" IS NULL) OR " +
                    "(\"status\" = 'Failed' AND " +
                    "\"claimed_at_utc\" IS NOT NULL AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NOT NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NOT NULL AND " +
                    "\"failure_message\" ~ '[^[:space:]]') OR " +
                    "(\"status\" = 'Cancelled' AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NULL)");
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
            .Property(job => job.ClaimedAtUtc)
            .HasColumnName("claimed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder
            .Property(job => job.ClaimToken)
            .HasColumnName("claim_token")
            .HasColumnType("uuid");

        builder
            .Property(job => job.LeaseExpiresAtUtc)
            .HasColumnName("lease_expires_at_utc")
            .HasColumnType("timestamp with time zone");

        builder
            .Property(job => job.SimulationId)
            .HasColumnName("simulation_id")
            .HasColumnType("uuid");

        builder
            .Property(job => job.FinishedAtUtc)
            .HasColumnName("finished_at_utc")
            .HasColumnType("timestamp with time zone");

        builder
            .Property(job => job.FailureMessage)
            .HasColumnName("failure_message")
            .HasMaxLength(
                SimulationJobResult.MaximumFailureMessageLength)
            .HasColumnType("character varying(1000)");

        builder
            .HasIndex(job => new
            {
                job.Status,
                job.CreatedAtUtc,
                job.Id
            })
            .HasDatabaseName(
                "ix_simulation_jobs_status_created_at_utc_id");

        builder
            .HasIndex(job => new
            {
                job.Status,
                job.LeaseExpiresAtUtc,
                job.Id
            })
            .HasDatabaseName(
                "ix_simulation_jobs_status_lease_expires_at_utc_id");

        builder
            .HasIndex(job => job.SimulationId)
            .HasDatabaseName(
                "ix_simulation_jobs_simulation_id");

        builder
            .HasOne<SimulationEntity>()
            .WithMany()
            .HasForeignKey(job => job.SimulationId)
            .HasConstraintName(
                "fk_simulation_jobs_simulations_simulation_id")
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}