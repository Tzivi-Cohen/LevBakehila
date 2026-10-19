namespace LevBakehila.Core.Settings;

// Passwords for the demo users - stored in User Secrets, never in source code
public class SeedSettings
{
    public const string SectionName = "Seed";

    public string AdminPassword { get; set; } = string.Empty;
    public string VolunteerPassword { get; set; } = string.Empty;
}
