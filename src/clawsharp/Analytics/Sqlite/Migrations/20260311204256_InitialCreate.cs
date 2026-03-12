using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Analytics.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Interactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UserPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    Thinking = table.Column<string>(type: "TEXT", nullable: true),
                    Response = table.Column<string>(type: "TEXT", nullable: false),
                    ToolCallsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ToolIterations = table.Column<int>(type: "INTEGER", nullable: false),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheReadTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CostUsd = table.Column<double>(type: "REAL", nullable: false),
                    CacheSavingsUsd = table.Column<double>(type: "REAL", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_Channel",
                table: "Interactions",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_Model",
                table: "Interactions",
                column: "Model");

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_SessionId",
                table: "Interactions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_Timestamp",
                table: "Interactions",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Interactions");
        }
    }
}
