using LevBakehila.Core.Enums;

namespace LevBakehila.Core.Entities;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Volunteer;
    public string Neighborhood { get; set; } = string.Empty;

    // Concurrency token protecting the volunteer's assignments.
    // Mapped to PostgreSQL's xmin system column, which changes on every UPDATE of the row.
    public uint Version { get; set; }

    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
    public ICollection<Skill> Skills { get; set; } = new List<Skill>();
}
