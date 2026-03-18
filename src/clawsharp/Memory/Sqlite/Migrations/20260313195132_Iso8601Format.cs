using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Memory.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Iso8601Format : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Ts",
                table: "History",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00'",
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT",
                oldDefaultValueSql: "datetime('now')");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedAt",
                table: "Facts",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00'",
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT",
                oldDefaultValueSql: "datetime('now')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "Ts",
                table: "History",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "datetime('now')",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValueSql: "strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00'");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Facts",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "datetime('now')",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValueSql: "strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00'");
        }
    }
}
