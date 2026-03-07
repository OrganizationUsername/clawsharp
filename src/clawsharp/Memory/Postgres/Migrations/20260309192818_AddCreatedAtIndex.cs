using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clawsharp.Memory.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Facts_CreatedAt",
                table: "Facts",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Facts_CreatedAt",
                table: "Facts");
        }
    }
}
