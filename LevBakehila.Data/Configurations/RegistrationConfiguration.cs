using LevBakehila.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LevBakehila.Data.Configurations;

public class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> builder)
    {
        builder.ToTable("Registrations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).IsRequired();

        // One-to-Many User -> Registration. Restrict = deleting a user never cascades.
        builder.HasOne(r => r.User)
            .WithMany(u => u.Registrations)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // One row per volunteer + activity. Cancel / re-register updates the same row.
        builder.HasIndex(r => new { r.UserId, r.ActivityId }).IsUnique();
    }
}
