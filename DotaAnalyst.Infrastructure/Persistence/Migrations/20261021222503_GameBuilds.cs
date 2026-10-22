using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaAnalyst.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameBuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_builds",
                columns: table => new
                {
                    commit_sha = table.Column<string>(type: "text", nullable: false),
                    client_version = table.Column<int>(type: "integer", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    files = table.Column<string>(type: "jsonb", nullable: false),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_builds", x => x.commit_sha);
                });

            migrationBuilder.CreateTable(
                name: "game_files",
                columns: table => new
                {
                    blob_sha = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_files", x => x.blob_sha);
                });

            migrationBuilder.CreateIndex(
                name: "ix_game_builds_released_at",
                table: "game_builds",
                column: "released_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_builds");

            migrationBuilder.DropTable(
                name: "game_files");
        }
    }
}
