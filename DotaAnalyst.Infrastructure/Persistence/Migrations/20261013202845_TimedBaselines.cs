using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaAnalyst.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimedBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stat_history",
                table: "baseline_metrics",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "timed_distributions",
                table: "baseline_distributions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stat_history",
                table: "baseline_metrics");

            migrationBuilder.DropColumn(
                name: "timed_distributions",
                table: "baseline_distributions");
        }
    }
}
