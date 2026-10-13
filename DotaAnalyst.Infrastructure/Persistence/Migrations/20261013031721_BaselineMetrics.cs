using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaAnalyst.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaselineMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "baseline_matches",
                columns: table => new
                {
                    match_id = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    start_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    tried_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_baseline_matches", x => x.match_id);
                });

            migrationBuilder.CreateTable(
                name: "baseline_metrics",
                columns: table => new
                {
                    match_id = table.Column<long>(type: "bigint", nullable: false),
                    hero_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    start_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    metrics = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_baseline_metrics", x => new { x.match_id, x.hero_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_baseline_matches_status_tried_at",
                table: "baseline_matches",
                columns: new[] { "status", "tried_at" });

            migrationBuilder.CreateIndex(
                name: "ix_baseline_metrics_hero_id_role_source_start_time",
                table: "baseline_metrics",
                columns: new[] { "hero_id", "role", "source", "start_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "baseline_matches");

            migrationBuilder.DropTable(
                name: "baseline_metrics");
        }
    }
}
