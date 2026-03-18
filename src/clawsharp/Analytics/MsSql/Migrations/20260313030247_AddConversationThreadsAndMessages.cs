using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Analytics.MsSql.Migrations
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
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "CacheSavingsUsd",
                table: "Interactions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AddColumn<long>(
                name: "ConversationThreadId",
                table: "Interactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationThreads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractionMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InteractionId = table.Column<long>(type: "bigint", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
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
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<double>(
                name: "CacheSavingsUsd",
                table: "Interactions",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);
        }
    }
}
