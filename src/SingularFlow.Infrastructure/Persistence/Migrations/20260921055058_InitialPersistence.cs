using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SingularFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "simulations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    singular_time = table.Column<double>(type: "double precision", nullable: false),
                    concentration_exponent = table.Column<double>(type: "double precision", nullable: false),
                    start_time = table.Column<double>(type: "double precision", nullable: false),
                    end_time = table.Column<double>(type: "double precision", nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: false),
                    sampling_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simulations", x => x.id);
                    table.CheckConstraint("ck_simulations_concentration_exponent", "\"concentration_exponent\" > 0 AND \"concentration_exponent\" < 0.01");
                    table.CheckConstraint("ck_simulations_end_time", "\"end_time\" > \"start_time\"");
                    table.CheckConstraint("ck_simulations_sample_count", "\"sample_count\" >= 2 AND \"sample_count\" <= 100000");
                    table.CheckConstraint("ck_simulations_singular_time", "\"singular_time\" > \"end_time\"");
                    table.CheckConstraint("ck_simulations_start_time", "\"start_time\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "simulation_states",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    simulation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    time = table.Column<double>(type: "double precision", nullable: false),
                    remaining_time = table.Column<double>(type: "double precision", nullable: false),
                    radial_length = table.Column<double>(type: "double precision", nullable: false),
                    axial_length = table.Column<double>(type: "double precision", nullable: false),
                    angular_velocity_scale = table.Column<double>(type: "double precision", nullable: false),
                    radial_velocity_scale = table.Column<double>(type: "double precision", nullable: false),
                    core_volume_scale = table.Column<double>(type: "double precision", nullable: false),
                    core_energy_scale = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simulation_states", x => x.id);
                    table.CheckConstraint("ck_simulation_states_sequence", "\"sequence\" >= 0");
                    table.ForeignKey(
                        name: "fk_simulation_states_simulations_simulation_id",
                        column: x => x.simulation_id,
                        principalTable: "simulations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_simulation_states_simulation_id_sequence",
                table: "simulation_states",
                columns: new[] { "simulation_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_simulations_created_at_utc",
                table: "simulations",
                column: "created_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "simulation_states");

            migrationBuilder.DropTable(
                name: "simulations");
        }
    }
}