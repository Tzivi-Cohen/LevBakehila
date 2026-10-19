using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LevBakehila.Data.Migrations
{
    /// <summary>
    /// Migration 3 - Many-to-Many VolunteerActivity <-> Skill (required skills per activity).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261019120000_AddActivitySkills")]
    public partial class AddActivitySkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivitySkills",
                columns: table => new
                {
                    ActivityId = table.Column<int>(type: "integer", nullable: false),
                    SkillId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySkills", x => new { x.ActivityId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_ActivitySkills_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivitySkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySkills_SkillId",
                table: "ActivitySkills",
                column: "SkillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivitySkills");
        }
    }
}
