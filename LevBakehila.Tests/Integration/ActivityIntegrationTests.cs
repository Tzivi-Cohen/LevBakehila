using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Data;
using LevBakehila.Data.Repositories;
using LevBakehila.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LevBakehila.Tests.Integration;

// Activity cancellation (Rule 8) and required skills (ActivitySkills) against a real PostgreSQL database
[Trait("Category", "Integration")]
public class ActivityIntegrationTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _database;

    public ActivityIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    private static ActivityService CreateActivityService(AppDbContext context)
        => new(new ActivityRepository(context), new RegistrationRepository(context), new UserRepository(context),
            new SkillRepository(context), new UnitOfWork(context), TestHelpers.Mapper, NullLogger<ActivityService>.Instance);

    private static RegistrationService CreateRegistrationService(AppDbContext context)
        => new(new RegistrationRepository(context), new ActivityRepository(context), new UserRepository(context),
            new UnitOfWork(context), TestHelpers.Mapper, NullLogger<RegistrationService>.Instance);

    [Fact]
    public async Task CancelActivity_KeepsRow_SetsInactive_AndCancelsActiveRegistrations()
    {
        var activity = await _database.AddActivityAsync(DateTime.UtcNow.AddDays(4), maxVolunteers: 3);
        var volunteer = await _database.AddVolunteerAsync($"cancel-{Guid.NewGuid():N}@test.local");

        await using (var context = _database.CreateContext())
        {
            await CreateRegistrationService(context).RegisterAsync(volunteer.Id, new CreateRegistrationDto { ActivityId = activity.Id });
        }

        await using (var context = _database.CreateContext())
        {
            await CreateActivityService(context).CancelActivityAsync(activity.Id);
        }

        await using var verify = _database.CreateContext();
        var saved = await verify.Activities.SingleAsync(a => a.Id == activity.Id);
        var registration = await verify.Registrations.SingleAsync(r => r.ActivityId == activity.Id);

        Assert.False(saved.IsActive);
        Assert.Equal(3, saved.AvailableSpots);
        Assert.Equal(RegistrationStatus.Cancelled, registration.Status);
    }

    [Fact]
    public async Task RequiredSkills_AreSaved_AndDeletingSkillRemovesOnlyTheLink()
    {
        Skill patience, music;
        await using (var context = _database.CreateContext())
        {
            patience = new Skill { Name = $"Patience {Guid.NewGuid():N}" };
            music = new Skill { Name = $"Music {Guid.NewGuid():N}" };
            context.Skills.AddRange(patience, music);
            await context.SaveChangesAsync();
        }

        var start = DateTime.UtcNow.Date.AddDays(6).AddHours(10);
        ActivityResponseDto created;
        await using (var context = _database.CreateContext())
        {
            created = await CreateActivityService(context).CreateAsync(new CreateActivityDto
            {
                Title = "Physiotherapy and breathing exercises",
                Description = "Help with physiotherapy and breathing exercises",
                Category = ActivityCategory.WeekdayAfternoon,
                Neighborhood = "Ramot",
                StartTime = start,
                EndTime = start.AddHours(2),
                MaxVolunteers = 2,
                RequiredSkillIds = new List<int> { patience.Id, music.Id }
            });
        }

        await using (var context = _database.CreateContext())
        {
            var loaded = await CreateActivityService(context).GetByIdAsync(created.Id);
            Assert.Equal(2, loaded.RequiredSkills.Count);

            context.Skills.Remove(await context.Skills.SingleAsync(s => s.Id == music.Id));
            await context.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        var activity = await verify.Activities.Include(a => a.RequiredSkills).SingleAsync(a => a.Id == created.Id);
        Assert.Equal(patience.Id, Assert.Single(activity.RequiredSkills).Id);
    }

    [Fact]
    public async Task MyNeighborhoodFirst_OrdersHomeNeighborhoodFirst_InTheDatabase_AcrossPages()
    {
        // A neighborhood name unique to this test, so other tests' activities never interfere
        string home = $"Home-{Guid.NewGuid():N}";
        string other = $"Other-{Guid.NewGuid():N}";
        var volunteer = await _database.AddVolunteerAsync($"near-{Guid.NewGuid():N}@test.local");
        await using (var context = _database.CreateContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == volunteer.Id);
            user.Neighborhood = home;
            await context.SaveChangesAsync();
        }

        var day = DateTime.UtcNow.Date.AddDays(500);
        await AddActivityInAsync(other, day.AddHours(8));    // earliest, but not at home
        await AddActivityInAsync(home, day.AddHours(12));
        await AddActivityInAsync(other, day.AddHours(9));
        await AddActivityInAsync(home, day.AddHours(10));

        await using var read = _database.CreateContext();
        var service = CreateActivityService(read);
        var filter = new ActivityFilterDto { MyNeighborhoodFirst = true, SortBy = "startTime", PageSize = 2, PageNumber = 1 };

        // Only this test's activities: filter by day through the two neighborhoods, page by page
        var page1 = await service.GetAllAsync(WithNeighborhoods(filter, home), volunteer.Id);
        Assert.All(page1.Items, a => Assert.Equal(home, a.Neighborhood));
        Assert.Equal(new[] { day.AddHours(10), day.AddHours(12) }, page1.Items.Select(a => a.StartTime));

        // Mixed list: the two home activities first (by time), then the others (by time) - on pages of 2
        var all = new List<ActivityResponseDto>();
        for (int pageNumber = 1; pageNumber <= 50; pageNumber++)
        {
            var page = await service.GetAllAsync(new ActivityFilterDto { MyNeighborhoodFirst = true, PageSize = 50, PageNumber = pageNumber }, volunteer.Id);
            all.AddRange(page.Items.Where(a => a.Neighborhood == home || a.Neighborhood == other));
            if (pageNumber >= page.TotalPages) break;
        }
        Assert.Equal(new[] { home, home, other, other }, all.Select(a => a.Neighborhood));
        Assert.Equal(new[] { day.AddHours(10), day.AddHours(12), day.AddHours(8), day.AddHours(9) }, all.Select(a => a.StartTime));
    }

    private static ActivityFilterDto WithNeighborhoods(ActivityFilterDto filter, string neighborhood)
    {
        filter.Neighborhood = neighborhood;
        return filter;
    }

    private async Task AddActivityInAsync(string neighborhood, DateTime start)
    {
        await using var context = _database.CreateContext();
        context.Activities.Add(new VolunteerActivity
        {
            Title = $"Near me {Guid.NewGuid():N}", Description = "Integration test", Category = ActivityCategory.SocialActivity,
            Neighborhood = neighborhood, StartTime = start, EndTime = start.AddHours(1), MaxVolunteers = 2, AvailableSpots = 2, IsActive = true
        });
        await context.SaveChangesAsync();
    }
}
