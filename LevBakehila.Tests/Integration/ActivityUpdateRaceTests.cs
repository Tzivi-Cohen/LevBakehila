using System.Reflection;
using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using LevBakehila.Data;
using LevBakehila.Data.Repositories;
using LevBakehila.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LevBakehila.Tests.Integration;

// Rule 6 + Rule 10 against a real PostgreSQL database:
// the coordinator moves activity X to new hours while a volunteer registered to X registers, at the same moment,
// to activity Y that overlaps X's NEW hours. Each request alone passes its own overlap check, so a check before
// SaveChanges is not enough - only the shared update of the volunteer's row (xmin) can stop the second one.
[Trait("Category", "Integration")]
public class ActivityUpdateRaceTests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly PostgresDatabaseFixture _database;

    public ActivityUpdateRaceTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task UpdateHours_WhileSameVolunteerRegistersToOverlappingActivity_BothChecksPass_OnlyOneSaves()
    {
        for (int round = 0; round < 5; round++)
        {
            var (x, y, volunteer, dto) = await ArrangeAsync(daysFromNow: 70 + round);

            // Both requests are held right after their overlap check has passed, then released together
            var adminChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var volunteerChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var adminTask = Task.Run(() => RunAsync(async context =>
            {
                var registrations = PauseAfterCheck.Wrap(new RegistrationRepository(context),
                    nameof(IRegistrationRepository.HasOverlapForRegisteredVolunteersAsync), adminChecked, release.Task);
                await CreateActivityService(context, registrations).UpdateAsync(x.Id, dto);
            }));

            var volunteerTask = Task.Run(() => RunAsync(async context =>
            {
                var registrations = PauseAfterCheck.Wrap(new RegistrationRepository(context),
                    nameof(IRegistrationRepository.HasOverlappingActiveRegistrationAsync), volunteerChecked, release.Task);
                await CreateRegistrationService(context, registrations).RegisterAsync(volunteer.Id, new CreateRegistrationDto { ActivityId = y.Id });
            }));

            await Task.WhenAll(adminChecked.Task, volunteerChecked.Task).WaitAsync(Timeout);
            release.SetResult();
            var results = await Task.WhenAll(adminTask, volunteerTask).WaitAsync(Timeout);

            // Exactly one wins; the other is rejected by the concurrency mechanism, not by a business check
            Assert.Single(results, r => r is null);
            Assert.Single(results, r => r is ConcurrencyConflictException);

            await AssertNoOverlapAsync(volunteer.Id);
        }
    }

    [Fact]
    public async Task UpdateHours_AndRegistration_RunInParallel_NeverLeaveAnOverlap()
    {
        for (int round = 0; round < 10; round++)
        {
            var (x, y, volunteer, dto) = await ArrangeAsync(daysFromNow: 90 + round);

            var results = await Task.WhenAll(
                Task.Run(() => RunAsync(context => CreateActivityService(context, new RegistrationRepository(context)).UpdateAsync(x.Id, dto))),
                Task.Run(() => RunAsync(context => CreateRegistrationService(context, new RegistrationRepository(context))
                    .RegisterAsync(volunteer.Id, new CreateRegistrationDto { ActivityId = y.Id }))));

            // Whatever the timing: at most one succeeds, the other gets a 409 (business or concurrency), never a 500
            Assert.Single(results, r => r is null);
            Assert.Single(results, r => r is ConflictException);

            await AssertNoOverlapAsync(volunteer.Id);
        }
    }

    // X: 10:00-12:00, volunteer registered. Y: 15:00-17:00, free. The coordinator moves X to 14:00-16:00 (overlaps Y).
    private async Task<(VolunteerActivity X, VolunteerActivity Y, User Volunteer, UpdateActivityDto Dto)> ArrangeAsync(int daysFromNow)
    {
        var day = DateTime.UtcNow.Date.AddDays(daysFromNow);
        var x = await _database.AddActivityAsync(day.AddHours(10), maxVolunteers: 3);
        var y = await _database.AddActivityAsync(day.AddHours(15), maxVolunteers: 3);
        var volunteer = await _database.AddVolunteerAsync($"rule6-{Guid.NewGuid():N}@test.local");

        await using (var context = _database.CreateContext())
        {
            await CreateRegistrationService(context, new RegistrationRepository(context))
                .RegisterAsync(volunteer.Id, new CreateRegistrationDto { ActivityId = x.Id });
        }

        var dto = new UpdateActivityDto
        {
            Title = x.Title,
            Description = x.Description,
            Category = x.Category,
            Neighborhood = x.Neighborhood,
            StartTime = day.AddHours(14),
            EndTime = day.AddHours(16),
            MaxVolunteers = x.MaxVolunteers
        };
        return (x, y, volunteer, dto);
    }

    private async Task AssertNoOverlapAsync(int userId)
    {
        await using var verify = _database.CreateContext();
        var active = await verify.Registrations
            .Where(r => r.UserId == userId && r.Status == RegistrationStatus.Active)
            .Select(r => new { r.Activity.StartTime, r.Activity.EndTime })
            .ToListAsync();

        foreach (var a in active)
        {
            foreach (var b in active.Where(b => !ReferenceEquals(a, b)))
            {
                Assert.False(a.StartTime < b.EndTime && b.StartTime < a.EndTime, "The volunteer ended up with overlapping registrations");
            }
        }
    }

    // Each call simulates a separate HTTP request: its own DbContext and its own services
    private async Task<Exception?> RunAsync(Func<AppDbContext, Task> action)
    {
        await using var context = _database.CreateContext();
        try
        {
            await action(context);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static ActivityService CreateActivityService(AppDbContext context, IRegistrationRepository registrations)
        => new(new ActivityRepository(context), registrations, new UserRepository(context),
            new SkillRepository(context), new UnitOfWork(context), TestHelpers.Mapper, NullLogger<ActivityService>.Instance);

    private static RegistrationService CreateRegistrationService(AppDbContext context, IRegistrationRepository registrations)
        => new(registrations, new ActivityRepository(context), new UserRepository(context),
            new UnitOfWork(context), TestHelpers.Mapper, NullLogger<RegistrationService>.Instance);
}

// Test-only proxy around the real repository: after the named overlap check returns, it signals and waits.
// Everything else goes straight to the real RegistrationRepository and the real database.
public class PauseAfterCheck : DispatchProxy
{
    private IRegistrationRepository _inner = null!;
    private string _methodName = string.Empty;
    private TaskCompletionSource _checked = null!;
    private Task _release = null!;

    public static IRegistrationRepository Wrap(IRegistrationRepository inner, string methodName, TaskCompletionSource @checked, Task release)
    {
        var proxy = Create<IRegistrationRepository, PauseAfterCheck>();
        var self = (PauseAfterCheck)(object)proxy;
        self._inner = inner;
        self._methodName = methodName;
        self._checked = @checked;
        self._release = release;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var result = targetMethod!.Invoke(_inner, args);
        return targetMethod.Name == _methodName && result is Task<bool> check ? PauseAsync(check) : result;
    }

    private async Task<bool> PauseAsync(Task<bool> check)
    {
        bool overlap = await check;
        _checked.TrySetResult();
        await _release;
        return overlap;
    }
}
