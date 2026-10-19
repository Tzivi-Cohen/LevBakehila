namespace LevBakehila.Core.Settings;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    // Secret - stored in User Secrets, never in appsettings.json
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 120;
}
