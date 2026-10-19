using LevBakehila.Core.Enums;

namespace LevBakehila.Core.Entities;

public class Registration
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ActivityId { get; set; }
    public DateTime RegisteredAt { get; set; }
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Active;

    public User User { get; set; } = null!;
    public VolunteerActivity Activity { get; set; } = null!;
}
