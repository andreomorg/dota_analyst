using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaAnalyst.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaneMatchupBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_baseline_distributions",
                table: "baseline_distributions");

            migrationBuilder.AddColumn<int>(
                name: "opponent_hero_id",
                table: "baseline_metrics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "opponent_hero_id",
                table: "baseline_distributions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "pk_baseline_distributions",
                table: "baseline_distributions",
                columns: new[] { "hero_id", "role", "opponent_hero_id", "source" });

            migrationBuilder.CreateIndex(
                name: "ix_baseline_metrics_hero_id_role_opponent_hero_id_source_start",
                table: "baseline_metrics",
                columns: new[] { "hero_id", "role", "opponent_hero_id", "source", "start_time" });

            // The rows collected before: the lane opponent is the enemy of the opposing role in the same match
            // (LaneMatchup.OpposingRole; the team is in the stored metrics).
            migrationBuilder.Sql("""
                UPDATE baseline_metrics AS mine
                SET opponent_hero_id = theirs.hero_id
                FROM baseline_metrics AS theirs
                WHERE theirs.match_id = mine.match_id
                  AND theirs.metrics->>'team' <> mine.metrics->>'team'
                  AND theirs.role = CASE mine.role WHEN 1 THEN 3 WHEN 3 THEN 1 WHEN 2 THEN 2 WHEN 4 THEN 5 WHEN 5 THEN 4 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_baseline_metrics_hero_id_role_opponent_hero_id_source_start",
                table: "baseline_metrics");

            migrationBuilder.DropPrimaryKey(
                name: "pk_baseline_distributions",
                table: "baseline_distributions");

            migrationBuilder.DropColumn(
                name: "opponent_hero_id",
                table: "baseline_metrics");

            migrationBuilder.DropColumn(
                name: "opponent_hero_id",
                table: "baseline_distributions");

            migrationBuilder.AddPrimaryKey(
                name: "pk_baseline_distributions",
                table: "baseline_distributions",
                columns: new[] { "hero_id", "role", "source" });
        }
    }
}
