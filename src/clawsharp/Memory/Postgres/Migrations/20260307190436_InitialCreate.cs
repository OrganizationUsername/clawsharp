using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace clawsharp.Memory.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Facts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    AccessCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    embedding_pgvec = table.Column<Vector>(type: "vector(1536)", nullable: true)
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
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Facts_embedding_pgvec",
                table: "Facts",
                column: "embedding_pgvec")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:ef_construction", 64)
                .Annotation("Npgsql:StorageParameter:m", 16);

            // WORM enforcement: database-level triggers prevent UPDATE/DELETE on History table
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_history_modification()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'HistoryEntry is append-only (WORM — Write Once, Read Many). % operations are not allowed. HistoryEntry records are immutable compaction snapshots of conversation history.', TG_OP;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_prevent_history_update
                    BEFORE UPDATE ON "History"
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_history_modification();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_prevent_history_delete
                    BEFORE DELETE ON "History"
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_history_modification();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS trg_prevent_history_update ON "History";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS trg_prevent_history_delete ON "History";""");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_history_modification();");

            migrationBuilder.DropTable(
                name: "Facts");

            migrationBuilder.DropTable(
                name: "History");
        }
    }
}
