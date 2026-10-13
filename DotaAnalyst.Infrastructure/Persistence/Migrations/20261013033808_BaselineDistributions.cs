using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaAnalyst.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaselineDistributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "baseline_distributions",
                columns: table => new
                {
                    hero_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    matches = table.Column<int>(type: "integer", nullable: false),
                    distributions = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_baseline_distributions", x => new { x.hero_id, x.role, x.source });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "baseline_distributions");
        }
    }
}
