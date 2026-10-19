using LevBakehila.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LevBakehila.Tests.Integration;

// Migration 4 (ConvertDateTimesToUtc) against a real PostgreSQL database, on its own temporary database:
// data written by the old schema (Israel local time) must keep the same moment after the migration.
[Trait("Category", "Integration")]
public class UtcMigrationTests
{
    private const string BeforeUtc = "20261019120000_AddActivitySkills";

    [Fact]
    public async Task ExistingIsraelLocalTimes_BecomeTheSameMomentInUtc_AndDownRestoresThem()
    {
        string connectionString = PostgresDatabaseFixture.NewTemporaryDatabaseConnectionString();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        var migrator = context.GetService<IMigrator>();

        try
        {
            // 1. The schema as it was before UTC, with rows the way the old code wrote them (Israel wall-clock time)
            await migrator.MigrateAsync(BeforeUtc);
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Activities" ("Title", "Description", "Category", "Neighborhood", "StartTime", "EndTime", "MaxVolunteers", "AvailableSpots", "IsActive")
                VALUES ('summer time', 'd', 1, 'Ramot', '2026-10-21 16:00:00', '2026-10-21 18:00:00', 2, 1, true),
                       ('winter time', 'd', 1, 'Ramot', '2026-10-25 17:00:00', '2026-10-25 19:00:00', 2, 2, true);
                INSERT INTO "Users" ("FullName", "Email", "PasswordHash", "Role", "Neighborhood")
                VALUES ('Old volunteer', 'old@test.local', 'hash', 2, 'Ramot');
                INSERT INTO "Registrations" ("UserId", "ActivityId", "RegisteredAt", "Status")
                SELECT u."Id", a."Id", '2026-10-19 13:43:42', 1 FROM "Users" u, "Activities" a WHERE a."Title" = 'summer time';
                """);

            // 2. Apply ConvertDateTimesToUtc
            await migrator.MigrateAsync();
            context.ChangeTracker.Clear();

            var summer = await context.Activities.AsNoTracking().SingleAsync(a => a.Title == "summer time");
            var winter = await context.Activities.AsNoTracking().SingleAsync(a => a.Title == "winter time");
            var registration = await context.Registrations.AsNoTracking().SingleAsync();

            // Israel summer time is UTC+3; winter time (after 25.10.2026 02:00) is UTC+2
            Assert.Equal(new DateTime(2026, 10, 21, 13, 0, 0, DateTimeKind.Utc), summer.StartTime);
            Assert.Equal(new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc), summer.EndTime);
            Assert.Equal(new DateTime(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc), winter.StartTime);
            Assert.Equal(new DateTime(2026, 10, 19, 10, 43, 42, DateTimeKind.Utc), registration.RegisteredAt);
            Assert.Equal(DateTimeKind.Utc, summer.StartTime.Kind);

            // Shown in Israel time, the hours are exactly what the users saw before
            var israel = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
            Assert.Equal(new DateTime(2026, 10, 21, 16, 0, 0), TimeZoneInfo.ConvertTimeFromUtc(summer.StartTime, israel));
            Assert.Equal(new DateTime(2026, 10, 25, 17, 0, 0), TimeZoneInfo.ConvertTimeFromUtc(winter.StartTime, israel));

            // 3. Down returns the same wall-clock values
            await migrator.MigrateAsync(BeforeUtc);
            var restored = await context.Database
                .SqlQueryRaw<string>("""SELECT "StartTime"::text AS "Value" FROM "Activities" ORDER BY "Title" """)
                .ToListAsync();
            Assert.Equal(new[] { "2026-10-21 16:00:00", "2026-10-25 17:00:00" }, restored);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
