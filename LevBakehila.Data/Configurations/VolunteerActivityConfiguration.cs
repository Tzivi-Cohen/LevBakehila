using LevBakehila.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LevBakehila.Data.Configurations;

public class VolunteerActivityConfiguration : IEntityTypeConfiguration<VolunteerActivity>
{
    public void Configure(EntityTypeBuilder<VolunteerActivity> builder)
    {
        // Database-level guarantee for Rule 2: 0 <= AvailableSpots <= MaxVolunteers
        builder.ToTable("Activities", table => table.HasCheckConstraint(
            "CK_Activities_AvailableSpots",
            "\"AvailableSpots\" >= 0 AND \"AvailableSpots\" <= \"MaxVolunteers\""));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).IsRequired().HasMaxLength(150);
        builder.Property(a => a.Description).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.Neighborhood).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Category).IsRequired();

        // Optimistic concurrency on the limited resource (PostgreSQL xmin)
        builder.Property(a => a.RowVersion).IsRowVersion();

        // One-to-Many VolunteerActivity -> Registration. Restrict = no cascade delete of history.
        builder.HasMany(a => a.Registrations)
            .WithOne(r => r.Activity)
            .HasForeignKey(r => r.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Many-to-Many VolunteerActivity <-> Skill through the ActivitySkills table.
        // Cascade only removes link rows, never activities or skills.
        builder.HasMany(a => a.RequiredSkills)
            .WithMany(s => s.Activities)
            .UsingEntity<Dictionary<string, object>>(
                "ActivitySkills",
                right => right.HasOne<Skill>().WithMany().HasForeignKey("SkillId").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<VolunteerActivity>().WithMany().HasForeignKey("ActivityId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("ActivitySkills");
                    join.HasKey("ActivityId", "SkillId");
                });
    }
}
