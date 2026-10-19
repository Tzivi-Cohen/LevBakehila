using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Data;
using LevBakehila.Data.Repositories;
using LevBakehila.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LevBakehila.Tests.Integration;

[Trait("Category", "Integration")]
public class ConcurrencyIntegrationTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _database;

    public ConcurrencyIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    private static RegistrationService CreateRegistrationService(AppDbContext context)
        => new(new RegistrationRepository(context), new ActivityRepository(context), new UserRepository(context),
            new UnitOfWork(context), TestHelpers.Mapper, NullLogger<RegistrationService>.Instance);

    // Each call simulates a separate HTTP request: its own DbContext and its own service instance
    private async Task<Exception?> TryRegisterAsync(int userId, int activityId)
    {
        await using var context = _database.CreateContext();
        try
        {
            await CreateRegistrationService(context).RegisterAsync(userId, new CreateRegistrationDto { ActivityId = activityId });
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task TwoContexts_SameActivity_SecondSaveThrowsDbUpdateConcurrencyException()
    {
        var activity = await _database.AddActivityAsync(DateTime.UtcNow.AddDays(3), maxVolunteers: 2);

        await using var context1 = _database.CreateContext();
        await using var context2 = _database.CreateContext();

        // 1. Both contexts read the same activity (same RowVersion)
        var copy1 = await context1.Activities.SingleAsync(a => a.Id == activity.Id);
        var copy2 = await context2.Activities.SingleAsync(a => a.Id == activity.Id);

        // 2. Both change the number of free spots
        copy1.AvailableSpots--;
        copy2.AvailableSpots--;

        // 3. The first save succeeds and PostgreSQL generates a new xmin (RowVersion)
        await context1.SaveChangesAsync();

        // 4. The second save still holds the old RowVersion => DbUpdateConcurrencyException
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context2.SaveChangesAsync());

        await using var verify = _database.CreateContext();
        Assert.Equal(1, (await verify.Activities.SingleAsync(a => a.Id == activity.Id)).AvailableSpots);
    }

    [Fact]
    public async Task TwoContexts_SameUser_SecondVersionUpdateThrowsDbUpdateConcurrencyException()
    {
        var user = await _database.AddVolunteerAsync($"version-{Guid.NewGuid():N}@test.local");

        await using var context1 = _database.CreateContext();
        await using var context2 = _database.CreateContext();
        var copy1 = await context1.Users.SingleAsync(u => u.Id == user.Id);
        var copy2 = await context2.Users.SingleAsync(u => u.Id == user.Id);

        new UserRepository(context1).MarkAsModified(copy1);
        new UserRepository(context2).MarkAsModified(copy2);

        await context1.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context2.SaveChangesAsync());
    }

    [Fact]
    public async Task TwoVolunteers_RaceForLastSpot_OnlyOneSucceeds()
    {
        // Repeated several times to give the race many chances to happen
        for (int round = 0; round < 5; round++)
        {
            var activity = await _database.AddActivityAsync(DateTime.UtcNow.AddDays(5 + round), maxVolunteers: 1);
            var volunteerA = await _database.AddVolunteerAsync($"a-{Guid.NewGuid():N}@test.local");
            var volunteerB = await _database.AddVolunteerAsync($"b-{Guid.NewGuid():N}@test.local");

            var results = await Task.WhenAll(
                Task.Run(() => TryRegisterAsync(volunteerA.Id, activity.Id)),
                Task.Run(() => TryRegisterAsync(volunteerB.Id, activity.Id)));

            Assert.Single(results, r => r is null);
            Assert.Single(results, r => r is ConflictException);

            await using var verify = _database.CreateContext();
            var saved = await verify.Activities.SingleAsync(a => a.Id == activity.Id);
            int activeCount = await verify.Registrations.CountAsync(r => r.ActivityId == activity.Id && r.Status == RegistrationStatus.Active);
            Assert.Equal(0, saved.AvailableSpots);
            Assert.Equal(1, activeCount);
        }
    }

    [Fact]
    public async Task SameVolunteer_ParallelRegistrationsToOverlappingActivities_OnlyOneSucceeds()
    {
        for (int round = 0; round < 5; round++)
        {
            var start = DateTime.UtcNow.AddDays(20 + round);
            var activity1 = await _database.AddActivityAsync(start, maxVolunteers: 5);
            var activity2 = await _database.AddActivityAsync(start.AddHours(1), maxVolunteers: 5); // overlaps activity1
            var volunteer = await _database.AddVolunteerAsync($"overlap-{Guid.NewGuid():N}@test.local");

            var results = await Task.WhenAll(
                Task.Run(() => TryRegisterAsync(volunteer.Id, activity1.Id)),
                Task.Run(() => TryRegisterAsync(volunteer.Id, activity2.Id)));

            Assert.Single(results, r => r is null);
            Assert.Single(results, r => r is ConflictException);

            await using var verify = _database.CreateContext();
            int activeCount = await verify.Registrations.CountAsync(r => r.UserId == volunteer.Id && r.Status == RegistrationStatus.Active);
            Assert.Equal(1, activeCount);
        }
    }

    [Fact]
    public async Task CancelRegistration_UpdatesStatusAndSpotsTogether()
    {
        var activity = await _database.AddActivityAsync(DateTime.UtcNow.AddDays(40), maxVolunteers: 2);
        var volunteer = await _database.AddVolunteerAsync($"cancel-{Guid.NewGuid():N}@test.local");

        int registrationId;
        await using (var context = _database.CreateContext())
        {
            registrationId = (await CreateRegistrationService(context)
                .RegisterAsync(volunteer.Id, new CreateRegistrationDto { ActivityId = activity.Id })).Id;
        }

        await using (var context = _database.CreateContext())
        {
            var service = CreateRegistrationService(context);
            await service.CancelAsync(registrationId, volunteer.Id, isAdmin: false);
        }

        // Second cancel must not add a spot again
        await using (var context = _database.CreateContext())
        {
            await CreateRegistrationService(context).CancelAsync(registrationId, volunteer.Id, isAdmin: false);
        }

        await using var verify = _database.CreateContext();
        var registration = await verify.Registrations.SingleAsync(r => r.Id == registrationId);
        var saved = await verify.Activities.SingleAsync(a => a.Id == activity.Id);
        Assert.Equal(RegistrationStatus.Cancelled, registration.Status);
        Assert.Equal(2, saved.AvailableSpots);
    }
}
