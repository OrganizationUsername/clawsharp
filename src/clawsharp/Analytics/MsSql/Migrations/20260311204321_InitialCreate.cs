using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Analytics.MsSql.Migrations
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Thinking = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Response = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToolCallsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToolIterations = table.Column<int>(type: "int", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<double>(type: "float", nullable: false),
                    CacheSavingsUsd = table.Column<double>(type: "float", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
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
