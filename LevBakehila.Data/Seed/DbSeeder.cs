using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Interfaces;
using LevBakehila.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LevBakehila.Data.Seed;

// Demo data. Every step checks what already exists, so running it again never creates duplicates.
// Passwords are NOT in the source code - they come from configuration (User Secrets).
public static class DbSeeder
{
    public const string AdminEmail = "admin@levbakehila.local";

    // Demo activities are planned in Israel time (e.g. 16:00) and stored in UTC
    private static readonly TimeZoneInfo IsraelTime = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    private static readonly string[] SkillNames =
    {
        "עזרה בשיעורי בית",
        "פיזיותרפיה ותרגילי נשימה",
        "הפעלה חברתית",
        "מוזיקה ויצירה",
        "ליווי בשבת"
    };

    private record VolunteerSeed(string Email, string FullName, string Neighborhood, string[] Skills);

    private static readonly VolunteerSeed[] Volunteers =
    {
        new("noa@levbakehila.local", "נועה כהן", "רמות", new[] { "עזרה בשיעורי בית", "הפעלה חברתית" }),
        new("shira@levbakehila.local", "שירה לוי", "גילה", new[] { "פיזיותרפיה ותרגילי נשימה" }),
        new("tamar@levbakehila.local", "תמר מזרחי", "רמות", new[] { "ליווי בשבת", "מוזיקה ויצירה" })
    };

    public static async Task SeedAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        SeedSettings settings,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SeedSkillsAsync(context, cancellationToken);
        await SeedUsersAsync(context, passwordHasher, settings, cancellationToken);
        await SeedActivitiesAsync(context, cancellationToken);
        await SeedRegistrationsAsync(context, cancellationToken);

        logger.LogInformation("Seed data verified");
    }

    private static async Task SeedSkillsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var existing = await context.Skills.Select(s => s.Name).ToListAsync(cancellationToken);
        foreach (var name in SkillNames.Except(existing))
        {
            context.Skills.Add(new Skill { Name = name });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedUsersAsync(AppDbContext context, IPasswordHasher passwordHasher, SeedSettings settings, CancellationToken cancellationToken)
    {
        var existingEmails = await context.Users.Select(u => u.Email).ToListAsync(cancellationToken);
        var skills = await context.Skills.ToListAsync(cancellationToken);

        if (!existingEmails.Contains(AdminEmail))
        {
            context.Users.Add(new User
            {
                FullName = "רכזת ראשית",
                Email = AdminEmail,
                Neighborhood = "מרכז העיר",
                Role = UserRole.Admin,
                PasswordHash = passwordHasher.Hash(RequireSetting(settings.AdminPassword, "Seed:AdminPassword"))
            });
        }

        foreach (var volunteer in Volunteers.Where(v => !existingEmails.Contains(v.Email)))
        {
            context.Users.Add(new User
            {
                FullName = volunteer.FullName,
                Email = volunteer.Email,
                Neighborhood = volunteer.Neighborhood,
                Role = UserRole.Volunteer,
                PasswordHash = passwordHasher.Hash(RequireSetting(settings.VolunteerPassword, "Seed:VolunteerPassword")),
                Skills = skills.Where(s => volunteer.Skills.Contains(s.Name)).ToList()
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedActivitiesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        // Today's date in Israel (not the server's date), as a wall-clock date without a time zone
        var day = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelTime).Date;
        var activities = new[]
        {
            NewActivity("עזרה בשיעורי בית - כיתה ד'", "עזרה בהכנת שיעורי בית בחשבון ובאנגלית.", ActivityCategory.HomeworkHelp, "רמות", day.AddDays(2).AddHours(16), 2, 2),
            NewActivity("ארוחת ליל שבת משפחתית", "הצטרפות לארוחת שבת ופעילות עם הילדים.", ActivityCategory.FridayNight, "גילה", day.AddDays(4).AddHours(18), 3, 3),
            NewActivity("שבת אחר הצהריים בפארק", "משחקים ופעילות חברתית בפארק השכונתי.", ActivityCategory.SaturdayAfternoon, "רמות", day.AddDays(5).AddHours(15), 2, 2),
            NewActivity("תרגילי פיזיותרפיה ונשימה", "ליווי ילד בתרגילי פיזיותרפיה ותרגילי נשימה לפי הנחיית המטפלת.", ActivityCategory.WeekdayAfternoon, "גילה", day.AddDays(3).AddHours(17), 1, 1),
            NewActivity("ערב יצירה ומוזיקה", "פעילות חברתית של יצירה ושירה בקבוצה.", ActivityCategory.SocialActivity, "מרכז העיר", day.AddDays(6).AddHours(17), 4, 2)
        };

        var existingTitles = await context.Activities.Select(a => a.Title).ToListAsync(cancellationToken);
        context.Activities.AddRange(activities.Where(a => !existingTitles.Contains(a.Title)));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedRegistrationsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var pairs = new[]
        {
            (Email: "noa@levbakehila.local", Title: "עזרה בשיעורי בית - כיתה ד'"),
            (Email: "noa@levbakehila.local", Title: "שבת אחר הצהריים בפארק"),
            (Email: "shira@levbakehila.local", Title: "תרגילי פיזיותרפיה ונשימה")
        };

        foreach (var pair in pairs)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == pair.Email, cancellationToken);
            var activity = await context.Activities.FirstOrDefaultAsync(a => a.Title == pair.Title, cancellationToken);
            if (user is null || activity is null || activity.AvailableSpots == 0)
            {
                continue;
            }

            bool exists = await context.Registrations.AnyAsync(r => r.UserId == user.Id && r.ActivityId == activity.Id, cancellationToken);
            if (exists)
            {
                continue;
            }

            context.Registrations.Add(new Registration
            {
                UserId = user.Id,
                ActivityId = activity.Id,
                RegisteredAt = DateTime.UtcNow,
                Status = RegistrationStatus.Active
            });
            activity.AvailableSpots--;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static VolunteerActivity NewActivity(string title, string description, ActivityCategory category,
        string neighborhood, DateTime israelStart, int maxVolunteers, int durationHours) => new()
    {
        Title = title,
        Description = description,
        Category = category,
        Neighborhood = neighborhood,
        StartTime = TimeZoneInfo.ConvertTimeToUtc(israelStart, IsraelTime),
        EndTime = TimeZoneInfo.ConvertTimeToUtc(israelStart.AddHours(durationHours), IsraelTime),
        MaxVolunteers = maxVolunteers,
        AvailableSpots = maxVolunteers,
        IsActive = true
    };

    private static string RequireSetting(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing configuration value '{key}' required to create the demo users. " +
                $"Set it with: dotnet user-secrets set \"{key}\" \"<password>\" --project LevBakehila.API");
        }
        return value;
    }
}
