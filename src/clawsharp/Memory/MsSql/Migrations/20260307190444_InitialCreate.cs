using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Memory.MsSql.Migrations
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    AccessCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "History",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ts = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                });

            // WORM enforcement: database-level triggers prevent UPDATE/DELETE on History table
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_prevent_history_update
                    ON History
                    INSTEAD OF UPDATE
                AS
                BEGIN
                    RAISERROR('HistoryEntry is append-only (WORM — Write Once, Read Many). UPDATE operations are not allowed. HistoryEntry records are immutable compaction snapshots of conversation history.', 16, 1);
                    ROLLBACK TRANSACTION;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_prevent_history_delete
                    ON History
                    INSTEAD OF DELETE
                AS
                BEGIN
                    RAISERROR('HistoryEntry is append-only (WORM — Write Once, Read Many). DELETE operations are not allowed. HistoryEntry records are immutable compaction snapshots of conversation history.', 16, 1);
                    ROLLBACK TRANSACTION;
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
