using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LevBakehila.Data.Migrations
{
    /// <summary>
    /// Migration 2 - concurrency tokens and unique indexes.
    /// The concurrency tokens map to PostgreSQL's xmin system column: EF records them in the model,
    /// and Npgsql emits no DDL for xmin because every PostgreSQL table already has it.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261019110000_AddConcurrencyAndIndexes")]
    public partial class AddConcurrencyAndIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The new unique (UserId, ActivityId) index also serves lookups by UserId
            migrationBuilder.DropIndex(
                name: "IX_Registrations_UserId",
                table: "Registrations");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Users",
                type: "xid",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Activities",
                type: "xid",
                rowVersion: true,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_UserId_ActivityId",
                table: "Registrations",
                columns: new[] { "UserId", "ActivityId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_UserId_ActivityId",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Activities");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_UserId",
                table: "Registrations",
                column: "UserId");
        }
    }
}
