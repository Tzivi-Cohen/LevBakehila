using LevBakehila.Core.Interfaces;
using LevBakehila.Core.Settings;
using LevBakehila.Data;
using LevBakehila.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LevBakehila.API.Configuration;

// Runs at startup: applies the Migrations, then the (idempotent) demo Seed.
public static class DatabaseInitializer
{
    private const int MaxMigrationAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    public static async Task InitializeAsync(IServiceProvider rootServices, IConfiguration configuration, ILogger logger)
    {
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<AppDbContext>();

        await MigrateAsync(dbContext, logger);

        // A Seed problem (e.g. missing Seed:AdminPassword) is logged but does not stop the API:
        // the schema is already in place, so registration, login and the other endpoints still work.
        try
        {
            var seedSettings = configuration.GetSection(SeedSettings.SectionName).Get<SeedSettings>() ?? new SeedSettings();
            await DbSeeder.SeedAsync(dbContext, services.GetRequiredService<IPasswordHasher>(), seedSettings, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Seed data was not applied. The API continues without the missing demo data");
        }
    }

    // A database that is still starting up (common right after a deploy) gets a few more chances.
    // Any other failure stops startup: running without the expected schema would break every request.
    private static async Task MigrateAsync(AppDbContext dbContext, ILogger logger)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Database migrations are up to date");
                return;
            }
            catch (NpgsqlException ex) when (attempt < MaxMigrationAttempts && ex.IsTransient)
            {
                logger.LogWarning("Database not reachable (attempt {Attempt} of {MaxAttempts}): {Error}. Retrying in {DelaySeconds} s",
                    attempt, MaxMigrationAttempts, ex.Message, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay);
            }
        }
    }
}
