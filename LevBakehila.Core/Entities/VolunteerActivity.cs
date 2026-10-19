using LevBakehila.Core.Enums;

namespace LevBakehila.Core.Entities;

public class VolunteerActivity
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ActivityCategory Category { get; set; }
    public string Neighborhood { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int MaxVolunteers { get; set; }
    public int AvailableSpots { get; set; }
    public bool IsActive { get; set; } = true;

    // Optimistic concurrency on the limited resource (AvailableSpots).
    // Mapped to PostgreSQL's xmin system column, which changes on every UPDATE of the row.
    public uint RowVersion { get; set; }

    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    // Skills the coordinator recommends for this activity (guidance only, does not block registration)
    public ICollection<Skill> RequiredSkills { get; set; } = new List<Skill>();
}
