using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingularFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationJobRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs");

            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "simulation_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_failed_at_utc",
                table: "simulation_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_failure_message",
                table: "simulation_jobs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_attempts",
                table: "simulation_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at_utc",
                table: "simulation_jobs",
                type: "timestamp with time zone",
                nullable: true,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.Sql(
                """
                UPDATE simulation_jobs
                SET
                    attempt_count =
                        CASE
                            WHEN claimed_at_utc IS NOT NULL THEN 1
                            ELSE 0
                        END,
                    max_attempts = 3,
                    next_attempt_at_utc =
                        CASE
                            WHEN status = 'Pending' THEN created_at_utc
                            ELSE NULL
                        END,
                    last_failed_at_utc = NULL,
                    last_failure_message = NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_simulation_jobs_retry_counters",
                table: "simulation_jobs",
                sql: "\"attempt_count\" >= 0 AND \"max_attempts\" BETWEEN 1 AND 10 AND \"attempt_count\" <= \"max_attempts\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_simulation_jobs_last_failure",
                table: "simulation_jobs",
                sql: "(\"last_failed_at_utc\" IS NULL AND \"last_failure_message\" IS NULL) OR (\"last_failed_at_utc\" IS NOT NULL AND \"last_failure_message\" IS NOT NULL AND \"last_failure_message\" ~ '[^[:space:]]')");

            migrationBuilder.DropIndex(
                name: "ix_simulation_jobs_status_created_at_utc_id",
                table: "simulation_jobs");

            migrationBuilder.CreateIndex(
                name: "ix_simulation_jobs_status_next_attempt_at_utc_created_at_utc_id",
                table: "simulation_jobs",
                columns: new[] { "status", "next_attempt_at_utc", "created_at_utc", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs",
                sql: "(\"status\" = 'Pending' AND \"claimed_at_utc\" IS NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL AND \"next_attempt_at_utc\" IS NOT NULL AND \"attempt_count\" < \"max_attempts\") OR (\"status\" = 'Running' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NOT NULL AND \"lease_expires_at_utc\" IS NOT NULL AND \"lease_expires_at_utc\" > \"claimed_at_utc\" AND \"next_attempt_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL AND \"attempt_count\" >= 1 AND \"attempt_count\" <= \"max_attempts\") OR (\"status\" = 'Completed' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"next_attempt_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL AND \"failure_message\" IS NULL AND \"attempt_count\" >= 1 AND \"attempt_count\" <= \"max_attempts\") OR (\"status\" = 'Failed' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"next_attempt_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NOT NULL AND \"failure_message\" ~ '[^[:space:]]' AND \"attempt_count\" >= 1 AND \"attempt_count\" <= \"max_attempts\") OR (\"status\" = 'Cancelled' AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"next_attempt_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL AND \"attempt_count\" >= 0 AND \"attempt_count\" <= \"max_attempts\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_simulation_jobs_last_failure",
                table: "simulation_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_simulation_jobs_retry_counters",
                table: "simulation_jobs");

            migrationBuilder.DropIndex(
                name: "ix_simulation_jobs_status_next_attempt_at_utc_created_at_utc_id",
                table: "simulation_jobs");

            migrationBuilder.CreateIndex(
                name: "ix_simulation_jobs_status_created_at_utc_id",
                table: "simulation_jobs",
                columns: new[] { "status", "created_at_utc", "id" });

            migrationBuilder.DropColumn(
                name: "last_failure_message",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "last_failed_at_utc",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "next_attempt_at_utc",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "max_attempts",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "simulation_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs",
                sql: "(\"status\" = 'Pending' AND \"claimed_at_utc\" IS NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Running' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NOT NULL AND \"lease_expires_at_utc\" IS NOT NULL AND \"lease_expires_at_utc\" > \"claimed_at_utc\" AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Completed' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Failed' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NOT NULL AND \"failure_message\" ~ '[^[:space:]]') OR (\"status\" = 'Cancelled' AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL)");
        }
    }
}