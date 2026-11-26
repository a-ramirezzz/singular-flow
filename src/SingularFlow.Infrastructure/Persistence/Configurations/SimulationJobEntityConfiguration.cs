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
                    "ck_simulation_jobs_retry_counters",
                    "\"attempt_count\" >= 0 AND " +
                    "\"max_attempts\" BETWEEN 1 AND 10 AND " +
                    "\"attempt_count\" <= \"max_attempts\"");

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_last_failure",
                    "(\"last_failed_at_utc\" IS NULL AND " +
                    "\"last_failure_message\" IS NULL) OR " +
                    "(\"last_failed_at_utc\" IS NOT NULL AND " +
                    "\"last_failure_message\" IS NOT NULL AND " +
                    "\"last_failure_message\" ~ '[^[:space:]]')");

                tableBuilder.HasCheckConstraint(
                    "ck_simulation_jobs_lifecycle",
                    "(\"status\" = 'Pending' AND " +
                    "\"claimed_at_utc\" IS NULL AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NULL AND " +
                    "\"next_attempt_at_utc\" IS NOT NULL AND " +
                    "\"attempt_count\" < \"max_attempts\") OR " +
                    "(\"status\" = 'Running' AND " +
                    "\"claimed_at_utc\" IS NOT NULL AND " +
                    "\"claim_token\" IS NOT NULL AND " +
                    "\"lease_expires_at_utc\" IS NOT NULL AND " +
                    "\"lease_expires_at_utc\" > \"claimed_at_utc\" AND " +
                    "\"next_attempt_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NULL AND " +
                    "\"attempt_count\" >= 1 AND " +
                    "\"attempt_count\" <= \"max_attempts\") OR " +
                    "(\"status\" = 'Completed' AND " +
                    "\"claimed_at_utc\" IS NOT NULL AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"next_attempt_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NOT NULL AND " +
                    "\"failure_message\" IS NULL AND " +
                    "\"attempt_count\" >= 1 AND " +
                    "\"attempt_count\" <= \"max_attempts\") OR " +
                    "(\"status\" = 'Failed' AND " +
                    "\"claimed_at_utc\" IS NOT NULL AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"next_attempt_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NOT NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NOT NULL AND " +
                    "\"failure_message\" ~ '[^[:space:]]' AND " +
                    "\"attempt_count\" >= 1 AND " +
                    "\"attempt_count\" <= \"max_attempts\") OR " +
                    "(\"status\" = 'Cancelled' AND " +
                    "\"claim_token\" IS NULL AND " +
                    "\"lease_expires_at_utc\" IS NULL AND " +
                    "\"next_attempt_at_utc\" IS NULL AND " +
                    "\"finished_at_utc\" IS NULL AND " +
                    "\"simulation_id\" IS NULL AND " +
                    "\"failure_message\" IS NULL AND " +
                    "\"attempt_count\" >= 0 AND " +
                    "\"attempt_count\" <= \"max_attempts\")");
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
            .Property(job => job.AttemptCount)
            .HasColumnName("attempt_count")
            .HasColumnType("integer")
            .HasDefaultValue(0)
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder
            .Property(job => job.MaxAttempts)
            .HasColumnName("max_attempts")
            .HasColumnType("integer")
            .HasDefaultValue(3)
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder
            .Property(job => job.NextAttemptAtUtc)
            .HasColumnName("next_attempt_at_utc")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd();

        builder
            .Property(job => job.LastFailedAtUtc)
            .HasColumnName("last_failed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder
            .Property(job => job.LastFailureMessage)
            .HasColumnName("last_failure_message")
            .HasMaxLength(1000)
            .HasColumnType("character varying(1000)");

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
                job.NextAttemptAtUtc,
                job.CreatedAtUtc,
                job.Id
            })
            .HasDatabaseName(
                "ix_simulation_jobs_status_next_attempt_at_utc_created_at_utc_id");

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