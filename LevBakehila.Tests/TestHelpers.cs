using AutoMapper;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Service.Mapping;

namespace LevBakehila.Tests;

internal static class TestHelpers
{
    // The same AutoMapper configuration the API registers with AddAutoMapper(typeof(MappingProfile))
    public static IMapper Mapper { get; } =
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    public static VolunteerActivity FutureActivity(int id = 1, int maxVolunteers = 2, int availableSpots = 2, int daysFromNow = 3)
    {
        var start = DateTime.UtcNow.Date.AddDays(daysFromNow).AddHours(16);
        return new VolunteerActivity
        {
            Id = id,
            Title = $"Activity {id}",
            Description = "Test activity",
            Category = ActivityCategory.HomeworkHelp,
            Neighborhood = "Ramot",
            StartTime = start,
            EndTime = start.AddHours(2),
            MaxVolunteers = maxVolunteers,
            AvailableSpots = availableSpots,
            IsActive = true
        };
    }

    public static User Volunteer(int id = 10) => new()
    {
        Id = id,
        FullName = $"Volunteer {id}",
        Email = $"volunteer{id}@test.local",
        Neighborhood = "Ramot",
        Role = UserRole.Volunteer,
        PasswordHash = "hash"
    };
}
