using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingularFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationJobLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs");

            migrationBuilder.AddColumn<Guid>(
                name: "claim_token",
                table: "simulation_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at_utc",
                table: "simulation_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE simulation_jobs
                SET
                    status = 'Pending',
                    claimed_at_utc = NULL
                WHERE status = 'Running';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_simulation_jobs_status_lease_expires_at_utc_id",
                table: "simulation_jobs",
                columns: new[] { "status", "lease_expires_at_utc", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs",
                sql: "(\"status\" = 'Pending' AND \"claimed_at_utc\" IS NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Running' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NOT NULL AND \"lease_expires_at_utc\" IS NOT NULL AND \"lease_expires_at_utc\" > \"claimed_at_utc\" AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Completed' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Failed' AND \"claimed_at_utc\" IS NOT NULL AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NOT NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NOT NULL AND \"failure_message\" ~ '[^[:space:]]') OR (\"status\" = 'Cancelled' AND \"claim_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_simulation_jobs_status_lease_expires_at_utc_id",
                table: "simulation_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "claim_token",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "lease_expires_at_utc",
                table: "simulation_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs",
                sql: "(\"status\" = 'Pending' AND \"claimed_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Running' AND \"claimed_at_utc\" IS NOT NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Completed' AND \"claimed_at_utc\" IS NOT NULL AND \"finished_at_utc\" IS NOT NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Failed' AND \"claimed_at_utc\" IS NOT NULL AND \"finished_at_utc\" IS NOT NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NOT NULL AND \"failure_message\" ~ '[^[:space:]]') OR (\"status\" = 'Cancelled' AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL)");
        }
    }
}