namespace LevBakehila.Core.Entities;

public class Skill
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<VolunteerActivity> Activities { get; set; } = new List<VolunteerActivity>();
}
