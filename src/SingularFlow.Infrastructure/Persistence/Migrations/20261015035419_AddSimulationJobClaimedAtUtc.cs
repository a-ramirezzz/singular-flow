using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingularFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationJobClaimedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at_utc",
                table: "simulation_jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claimed_at_utc",
                table: "simulation_jobs");
        }
    }
}