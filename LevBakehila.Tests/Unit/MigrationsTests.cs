using LevBakehila.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LevBakehila.Tests.Unit;

public class MigrationsTests
{
    // No database is opened: the provider is only needed to build the relational model
    private static AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options);

    [Fact]
    public void Migrations_ContainAllMigrations_InOrder()
    {
        using var context = CreateContext();

        var migrations = context.Database.GetMigrations().ToList();

        Assert.Equal(new[]
        {
            "20261019100000_InitialCreate",
            "20261019110000_AddConcurrencyAndIndexes",
            "20261019120000_AddActivitySkills",
            "20261019160000_ConvertDateTimesToUtc"
        }, migrations);
    }

    // Same check that "dotnet ef migrations add" does: the snapshot must match the current model,
    // otherwise a new migration would be generated
    [Fact]
    public void ModelSnapshot_MatchesCurrentModel_NoPendingChanges()
    {
        using var context = CreateContext();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        var initializer = context.GetService<IModelRuntimeInitializer>();
        var snapshotModel = initializer.Initialize(((IMutableModel)snapshot!.Model).FinalizeModel());
        var currentModel = context.GetService<IDesignTimeModel>().Model;

        var differences = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

        Assert.Empty(differences);
    }

    // UTC everywhere: every DateTime column is timestamptz, so a non-UTC DateTime can never be saved by mistake
    [Fact]
    public void AllDateTimeColumns_AreTimestampWithTimeZone()
    {
        using var context = CreateContext();

        var dateColumns = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime))
            .ToList();

        Assert.Equal(3, dateColumns.Count);
        Assert.All(dateColumns, p => Assert.Equal("timestamp with time zone", p.GetColumnType()));
    }
}
