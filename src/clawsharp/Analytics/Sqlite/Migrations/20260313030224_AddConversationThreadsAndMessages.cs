using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Analytics.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationThreadsAndMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "CostUsd",
                table: "Interactions",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "CacheSavingsUsd",
                table: "Interactions",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AddColumn<long>(
                name: "ConversationThreadId",
                table: "Interactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationThreads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractionMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InteractionId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractionMessages_Interactions_InteractionId",
                        column: x => x.InteractionId,
                        principalTable: "Interactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_ConversationThreadId",
                table: "Interactions",
                column: "ConversationThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationThreads_SessionId",
                table: "ConversationThreads",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractionMessages_InteractionId",
                table: "InteractionMessages",
                column: "InteractionId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionMessages_MessageType",
                table: "InteractionMessages",
                column: "MessageType");

            migrationBuilder.AddForeignKey(
                name: "FK_Interactions_ConversationThreads_ConversationThreadId",
                table: "Interactions",
                column: "ConversationThreadId",
                principalTable: "ConversationThreads",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Interactions_ConversationThreads_ConversationThreadId",
                table: "Interactions");

            migrationBuilder.DropTable(
                name: "ConversationThreads");

            migrationBuilder.DropTable(
                name: "InteractionMessages");

            migrationBuilder.DropIndex(
                name: "IX_Interactions_ConversationThreadId",
                table: "Interactions");

            migrationBuilder.DropColumn(
                name: "ConversationThreadId",
                table: "Interactions");

            migrationBuilder.AlterColumn<double>(
                name: "CostUsd",
                table: "Interactions",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<double>(
                name: "CacheSavingsUsd",
                table: "Interactions",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 6);
        }
    }
}
