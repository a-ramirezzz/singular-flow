using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingularFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationJobTerminalTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failure_message",
                table: "simulation_jobs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finished_at_utc",
                table: "simulation_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "simulation_id",
                table: "simulation_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_simulation_jobs_simulation_id",
                table: "simulation_jobs",
                column: "simulation_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs",
                sql: "(\"status\" = 'Pending' AND \"claimed_at_utc\" IS NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Running' AND \"claimed_at_utc\" IS NOT NULL AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Completed' AND \"claimed_at_utc\" IS NOT NULL AND \"finished_at_utc\" IS NOT NULL AND \"failure_message\" IS NULL) OR (\"status\" = 'Failed' AND \"claimed_at_utc\" IS NOT NULL AND \"finished_at_utc\" IS NOT NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NOT NULL AND \"failure_message\" ~ '[^[:space:]]') OR (\"status\" = 'Cancelled' AND \"finished_at_utc\" IS NULL AND \"simulation_id\" IS NULL AND \"failure_message\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_simulation_jobs_simulations_simulation_id",
                table: "simulation_jobs",
                column: "simulation_id",
                principalTable: "simulations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_simulation_jobs_simulations_simulation_id",
                table: "simulation_jobs");

            migrationBuilder.DropIndex(
                name: "ix_simulation_jobs_simulation_id",
                table: "simulation_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_simulation_jobs_lifecycle",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "failure_message",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "finished_at_utc",
                table: "simulation_jobs");

            migrationBuilder.DropColumn(
                name: "simulation_id",
                table: "simulation_jobs");
        }
    }
}