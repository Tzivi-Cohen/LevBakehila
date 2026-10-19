using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using LevBakehila.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LevBakehila.Tests.Unit;

public class ActivityServiceTests
{
    private readonly Mock<IActivityRepository> _activities = new();
    private readonly Mock<IRegistrationRepository> _registrations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISkillRepository> _skills = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITransaction> _transaction = new();
    private readonly ActivityService _service;

    public ActivityServiceTests()
    {
        _unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_transaction.Object);
        _skills.Setup(s => s.GetByIdsForUpdateAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Skill>());
        _service = new ActivityService(_activities.Object, _registrations.Object, _users.Object, _skills.Object,
            _unitOfWork.Object, TestHelpers.Mapper, NullLogger<ActivityService>.Instance);
    }

    private static CreateActivityDto ValidCreateDto()
    {
        var start = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        return new CreateActivityDto
        {
            Title = "Homework help",
            Description = "Math and English",
            Category = ActivityCategory.HomeworkHelp,
            Neighborhood = "Ramot",
            StartTime = start,
            EndTime = start.AddHours(2),
            MaxVolunteers = 3
        };
    }

    // Mirrors GetByIdWithActiveRegistrationsForUpdateAsync: the activity comes with its active registrations + volunteers
    private VolunteerActivity SetupActivityWithRegistrations(VolunteerActivity activity, int activeRegistrations)
    {
        for (int i = 1; i <= activeRegistrations; i++)
        {
            var volunteer = TestHelpers.Volunteer(100 + i);
            activity.Registrations.Add(new Registration
            {
                Id = i, UserId = volunteer.Id, User = volunteer, ActivityId = activity.Id, Activity = activity,
                Status = RegistrationStatus.Active
            });
        }
        _activities.Setup(a => a.GetByIdWithActiveRegistrationsForUpdateAsync(activity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(activity);
        return activity;
    }

    private static UpdateActivityDto UpdateDtoFrom(VolunteerActivity activity) => new()
    {
        Title = activity.Title,
        Description = activity.Description,
        Category = activity.Category,
        Neighborhood = activity.Neighborhood,
        StartTime = activity.StartTime,
        EndTime = activity.EndTime,
        MaxVolunteers = activity.MaxVolunteers
    };

    [Fact]
    public async Task Create_Valid_SetsAvailableSpotsToMax_AndSaves()
    {
        VolunteerActivity? saved = null;
        _activities.Setup(a => a.AddAsync(It.IsAny<VolunteerActivity>(), It.IsAny<CancellationToken>()))
            .Callback<VolunteerActivity, CancellationToken>((activity, _) => saved = activity);

        var result = await _service.CreateAsync(ValidCreateDto());

        Assert.NotNull(saved);
        Assert.True(saved!.IsActive);
        Assert.Equal(3, saved.AvailableSpots);
        Assert.Equal(3, result.AvailableSpots);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_EndBeforeStart_ThrowsBusinessRule()
    {
        var dto = ValidCreateDto();
        dto.EndTime = dto.StartTime.AddHours(-1);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.CreateAsync(dto));
        _activities.Verify(a => a.AddAsync(It.IsAny<VolunteerActivity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Valid_UpdatesFields_AndRecalculatesSpots()
    {
        var activity = SetupActivityWithRegistrations(TestHelpers.FutureActivity(maxVolunteers: 2, availableSpots: 1), activeRegistrations: 1);

        var dto = UpdateDtoFrom(activity);
        dto.Title = "Updated title";
        dto.MaxVolunteers = 5;

        var result = await _service.UpdateAsync(activity.Id, dto);

        Assert.Equal("Updated title", result.Title);
        Assert.Equal(5, activity.MaxVolunteers);
        Assert.Equal(4, activity.AvailableSpots);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        // Same hours => the volunteers' rows are not touched
        _users.Verify(u => u.MarkAsModified(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Update_QuotaBelowActiveRegistrations_ThrowsConflict()
    {
        var activity = SetupActivityWithRegistrations(TestHelpers.FutureActivity(maxVolunteers: 3, availableSpots: 0), activeRegistrations: 3);

        var dto = UpdateDtoFrom(activity);
        dto.MaxVolunteers = 2;

        await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(activity.Id, dto));

        Assert.Equal(3, activity.MaxVolunteers);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_NewHoursOverlapRegisteredVolunteer_ThrowsConflict()
    {
        var activity = SetupActivityWithRegistrations(TestHelpers.FutureActivity(maxVolunteers: 2, availableSpots: 1), activeRegistrations: 1);
        var originalStart = activity.StartTime;

        var dto = UpdateDtoFrom(activity);
        dto.StartTime = activity.StartTime.AddHours(3);
        dto.EndTime = activity.EndTime.AddHours(3);
        _registrations.Setup(r => r.HasOverlapForRegisteredVolunteersAsync(activity.Id, dto.StartTime, dto.EndTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(activity.Id, dto));

        Assert.Equal(originalStart, activity.StartTime);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelActivity_SetsInactiveAndCancelsActiveRegistrations_InTransaction()
    {
        var activity = SetupActivityWithRegistrations(TestHelpers.FutureActivity(maxVolunteers: 2, availableSpots: 0), activeRegistrations: 2);

        await _service.CancelActivityAsync(activity.Id);

        Assert.False(activity.IsActive);
        Assert.Equal(2, activity.AvailableSpots);
        Assert.All(activity.Registrations, r => Assert.Equal(RegistrationStatus.Cancelled, r.Status));
        _users.Verify(u => u.MarkAsModified(It.IsAny<User>()), Times.Exactly(2));
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _activities.Verify(a => a.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WithRequiredSkills_LinksSkillsToActivity()
    {
        var skills = new List<Skill> { new() { Id = 1, Name = "Patience" }, new() { Id = 2, Name = "Music" } };
        _skills.Setup(s => s.GetByIdsForUpdateAsync(It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(skills);

        var dto = ValidCreateDto();
        dto.RequiredSkillIds = new List<int> { 1, 2, 2 };

        var result = await _service.CreateAsync(dto);

        Assert.Equal(new[] { 1, 2 }, result.RequiredSkills.Select(s => s.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Create_WithUnknownSkill_ThrowsBusinessRule()
    {
        var dto = ValidCreateDto();
        dto.RequiredSkillIds = new List<int> { 99 };

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.CreateAsync(dto));
        _activities.Verify(a => a.AddAsync(It.IsAny<VolunteerActivity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_ReplacesRequiredSkills()
    {
        var activity = SetupActivityWithRegistrations(TestHelpers.FutureActivity(maxVolunteers: 2, availableSpots: 2), activeRegistrations: 0);
        activity.RequiredSkills.Add(new Skill { Id = 1, Name = "Patience" });
        _skills.Setup(s => s.GetByIdsForUpdateAsync(It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 1 && ids.Contains(3)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Skill> { new() { Id = 3, Name = "Sport" } });

        var dto = UpdateDtoFrom(activity);
        dto.RequiredSkillIds = new List<int> { 3 };

        var result = await _service.UpdateAsync(activity.Id, dto);

        Assert.Equal(3, Assert.Single(activity.RequiredSkills).Id);
        Assert.Equal("Sport", Assert.Single(result.RequiredSkills).Name);
    }

    [Fact]
    public async Task Update_NewHours_TouchesEveryRegisteredVolunteerVersion_InOneTransaction()
    {
        var activity = SetupActivityWithRegistrations(TestHelpers.FutureActivity(maxVolunteers: 3, availableSpots: 1), activeRegistrations: 2);
        var volunteers = activity.Registrations.Select(r => r.User).ToList();

        var dto = UpdateDtoFrom(activity);
        dto.StartTime = activity.StartTime.AddHours(1);
        dto.EndTime = activity.EndTime.AddHours(1);

        await _service.UpdateAsync(activity.Id, dto);

        // Rule 6 under concurrency: a parallel registration of these volunteers elsewhere hits the same rows
        foreach (var volunteer in volunteers)
        {
            _users.Verify(u => u.MarkAsModified(volunteer), Times.Once);
        }
        _unitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ConcurrencyConflict_RollsBackAndRethrows()
    {
        var activity = SetupActivityWithRegistrations(TestHelpers.FutureActivity(maxVolunteers: 2, availableSpots: 1), activeRegistrations: 1);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("conflict"));

        var dto = UpdateDtoFrom(activity);
        dto.StartTime = activity.StartTime.AddHours(1);
        dto.EndTime = activity.EndTime.AddHours(1);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _service.UpdateAsync(activity.Id, dto));

        _transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_CancelledActivity_ThrowsBusinessRule()
    {
        var activity = TestHelpers.FutureActivity();
        activity.IsActive = false;
        SetupActivityWithRegistrations(activity, activeRegistrations: 0);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateAsync(activity.Id, UpdateDtoFrom(activity)));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAll_MyNeighborhoodFirst_UsesTheVolunteersHomeNeighborhoodFromHerProfile()
    {
        var volunteer = TestHelpers.Volunteer(10);
        volunteer.Neighborhood = "Gilo";
        _users.Setup(u => u.GetByIdAsync(volunteer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(volunteer);
        _activities.Setup(a => a.GetPagedAsync(It.IsAny<ActivityFilterDto>(), null, "Gilo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<VolunteerActivity> { Items = new[] { TestHelpers.FutureActivity() }, PageNumber = 1, PageSize = 10, TotalCount = 1 });

        var result = await _service.GetAllAsync(new ActivityFilterDto { MyNeighborhoodFirst = true }, volunteer.Id);

        Assert.Single(result.Items);
        _activities.Verify(a => a.GetPagedAsync(It.IsAny<ActivityFilterDto>(), null, "Gilo", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithoutMyNeighborhoodFirst_DoesNotLoadTheUser_AndUsesNoPreference()
    {
        _activities.Setup(a => a.GetPagedAsync(It.IsAny<ActivityFilterDto>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<VolunteerActivity> { PageNumber = 1, PageSize = 10 });

        await _service.GetAllAsync(new ActivityFilterDto { OnlyAvailable = true }, currentUserId: 10);

        _users.Verify(u => u.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        // OnlyAvailable => only activities that start after "now" (UTC)
        _activities.Verify(a => a.GetPagedAsync(It.IsAny<ActivityFilterDto>(),
            It.Is<DateTime?>(d => d.HasValue && d.Value.Kind == DateTimeKind.Utc), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
