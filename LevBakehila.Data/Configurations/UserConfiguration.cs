using LevBakehila.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LevBakehila.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256);
        builder.Property(u => u.Neighborhood).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Role).IsRequired();

        // PostgreSQL xmin: a system column that changes automatically on every UPDATE of the row
        builder.Property(u => u.Version).IsRowVersion();

        builder.HasIndex(u => u.Email).IsUnique();

        // Many-to-Many User <-> Skill through the UserSkills table
        builder.HasMany(u => u.Skills)
            .WithMany(s => s.Users)
            .UsingEntity<Dictionary<string, object>>(
                "UserSkills",
                right => right.HasOne<Skill>().WithMany().HasForeignKey("SkillId").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<User>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("UserSkills");
                    join.HasKey("UserId", "SkillId");
                });
    }
}
