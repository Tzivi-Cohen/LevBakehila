using LevBakehila.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LevBakehila.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<VolunteerActivity> Activities => Set<VolunteerActivity>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Skill> Skills => Set<Skill>();

    // Dates are UTC everywhere (DateTime.UtcNow). Npgsql maps DateTime to "timestamp with time zone" (timestamptz),
    // which accepts only DateTimeKind.Utc - a local DateTime fails on save instead of being stored wrong.
    // Conversion to Israel time (Asia/Jerusalem) happens only in the client, for display.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Loads every IEntityTypeConfiguration in the Configurations folder
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
