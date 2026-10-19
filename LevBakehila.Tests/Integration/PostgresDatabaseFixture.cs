using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LevBakehila.Tests.Integration;

// Creates a real, temporary PostgreSQL database through the real Migrations, and drops only that
// temporary database at the end. EF Core InMemory does NOT support concurrency tokens (xmin).
// The server connection is taken from the environment variable LEVBAKEHILA_TEST_POSTGRES, or else from the
// API's User Secrets (ConnectionStrings:DefaultConnection). Its Database= is always replaced by a temporary
// name, so the real application database is never touched. Passwords are never stored in source code.
// The user needs the CREATEDB privilege.
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "LEVBAKEHILA_TEST_POSTGRES";

    // Same UserSecretsId as LevBakehila.API.csproj
    private const string ApiUserSecretsId = "levbakehila-api-6f2c1d9e";

    public string ConnectionString { get; }

    public PostgresDatabaseFixture()
    {
        ConnectionString = NewTemporaryDatabaseConnectionString();
    }

    // The configured server, with Database= replaced by a new temporary name (the database itself is not created here)
    public static string NewTemporaryDatabaseConnectionString()
    {
        string server = Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).Build().GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Integration tests need a real PostgreSQL server. Set ConnectionStrings:DefaultConnection in the API's " +
                $"User Secrets, or set the environment variable {EnvironmentVariable}.");

        return new NpgsqlConnectionStringBuilder(server)
        {
            Database = $"levbakehila_tests_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    public AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public async Task<User> AddVolunteerAsync(string email)
    {
        await using var context = CreateContext();
        var user = new User
        {
            FullName = "Integration Volunteer",
            Email = email,
            Neighborhood = "Ramot",
            Role = UserRole.Volunteer,
            PasswordHash = "not-used"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public async Task<VolunteerActivity> AddActivityAsync(DateTime start, int maxVolunteers)
    {
        await using var context = CreateContext();
        var activity = new VolunteerActivity
        {
            Title = $"Integration activity {Guid.NewGuid():N}",
            Description = "Integration test",
            Category = ActivityCategory.SocialActivity,
            Neighborhood = "Ramot",
            StartTime = start,
            EndTime = start.AddHours(2),
            MaxVolunteers = maxVolunteers,
            AvailableSpots = maxVolunteers,
            IsActive = true
        };
        context.Activities.Add(activity);
        await context.SaveChangesAsync();
        return activity;
    }
}
