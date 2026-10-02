using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingularFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "simulation_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    singular_time = table.Column<double>(type: "double precision", nullable: false),
                    concentration_exponent = table.Column<double>(type: "double precision", nullable: false),
                    start_time = table.Column<double>(type: "double precision", nullable: false),
                    end_time = table.Column<double>(type: "double precision", nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: false),
                    sampling_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simulation_jobs", x => x.id);
                    table.CheckConstraint("ck_simulation_jobs_concentration_exponent", "\"concentration_exponent\" > 0 AND \"concentration_exponent\" < 0.01");
                    table.CheckConstraint("ck_simulation_jobs_end_time", "\"end_time\" > \"start_time\"");
                    table.CheckConstraint("ck_simulation_jobs_sample_count", "\"sample_count\" >= 2 AND \"sample_count\" <= 100000");
                    table.CheckConstraint("ck_simulation_jobs_singular_time", "\"singular_time\" > \"end_time\"");
                    table.CheckConstraint("ck_simulation_jobs_start_time", "\"start_time\" >= 0");
                    table.CheckConstraint("ck_simulation_jobs_status", "\"status\" IN ('Pending', 'Running', 'Completed', 'Failed', 'Cancelled')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_simulation_jobs_status_created_at_utc_id",
                table: "simulation_jobs",
                columns: new[] { "status", "created_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "simulation_jobs");
        }
    }
}