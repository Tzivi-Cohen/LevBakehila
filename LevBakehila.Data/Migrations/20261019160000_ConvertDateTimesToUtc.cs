using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LevBakehila.Data.Migrations
{
    /// <summary>
    /// Migration 4 - all dates move to UTC (timestamp with time zone).
    /// The existing values were Israel local time (timestamp without time zone). "AT TIME ZONE 'Asia/Jerusalem'"
    /// reads each one as Israel time - with the right offset for its own date, summer or winter time - so every
    /// activity keeps the exact same moment and is still shown to the users at the same hour.
    /// The zone is written explicitly so the result never depends on the server's TimeZone setting.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261019160000_ConvertDateTimesToUtc")]
    public partial class ConvertDateTimesToUtc : Migration
    {
        private static readonly (string Table, string Column)[] DateColumns =
        {
            ("Activities", "StartTime"),
            ("Activities", "EndTime"),
            ("Registrations", "RegisteredAt")
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in DateColumns)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" TYPE timestamp with time zone " +
                    $"USING \"{column}\" AT TIME ZONE 'Asia/Jerusalem';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to Israel local time, the same moment
            foreach (var (table, column) in DateColumns)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" TYPE timestamp without time zone " +
                    $"USING \"{column}\" AT TIME ZONE 'Asia/Jerusalem';");
            }
        }
    }
}
