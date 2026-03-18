using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Memory.Sqlite.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Recreates WORM triggers on the History table. The Iso8601Format migration's AlterColumn
    /// on History.Ts caused SQLite to rebuild the table (CREATE → copy → DROP → RENAME),
    /// which silently destroyed the triggers created in InitialCreate.
    /// </summary>
    public partial class RestoreWormTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
