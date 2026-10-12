using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaAnalyst.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "replay_analyses",
                columns: table => new
                {
                    match_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_replay_analyses", x => x.match_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "replay_analyses");
        }
    }
}
