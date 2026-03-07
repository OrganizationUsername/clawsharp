using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Memory.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Facts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    AccessCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "History",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Ts = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                });

            // WORM enforcement: database-level triggers prevent UPDATE/DELETE on History table
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_prevent_history_update
                    BEFORE UPDATE ON History
                    BEGIN
                        SELECT RAISE(ABORT, 'HistoryEntry is append-only (WORM). UPDATE operations are not allowed.');
                    END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_prevent_history_delete
                    BEFORE DELETE ON History
                    BEGIN
                        SELECT RAISE(ABORT, 'HistoryEntry is append-only (WORM). DELETE operations are not allowed.');
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_prevent_history_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_prevent_history_delete;");

            migrationBuilder.DropTable(
                name: "Facts");

            migrationBuilder.DropTable(
                name: "History");
        }
    }
}
