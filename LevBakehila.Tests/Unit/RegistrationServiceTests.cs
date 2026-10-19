using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using LevBakehila.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LevBakehila.Tests.Unit;

public class RegistrationServiceTests
{
    private readonly Mock<IRegistrationRepository> _registrations = new();
    private readonly Mock<IActivityRepository> _activities = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITransaction> _transaction = new();
    private readonly RegistrationService _service;

    private readonly User _volunteer = TestHelpers.Volunteer(10);

    public RegistrationServiceTests()
    {
        _unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_transaction.Object);
        _users.Setup(u => u.GetByIdForUpdateAsync(_volunteer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_volunteer);

        _service = new RegistrationService(_registrations.Object, _activities.Object, _users.Object,
            _unitOfWork.Object, TestHelpers.Mapper, NullLogger<RegistrationService>.Instance);
    }

    private void SetupActivity(VolunteerActivity activity)
        => _activities.Setup(a => a.GetByIdForUpdateAsync(activity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(activity);

    private Task<RegistrationResponseDto> Register(int activityId)
        => _service.RegisterAsync(_volunteer.Id, new CreateRegistrationDto { ActivityId = activityId });

    private void VerifyNothingSaved()
    {
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_Valid_CreatesRegistration_DecrementsSpots_AndTouchesUserVersion()
    {
        var activity = TestHelpers.FutureActivity(availableSpots: 2);
        SetupActivity(activity);

        var result = await Register(activity.Id);

        Assert.Equal(RegistrationStatus.Active, result.Status);
        Assert.Equal(activity.Title, result.ActivityTitle);
        Assert.Equal(1, activity.AvailableSpots);
        _registrations.Verify(r => r.AddAsync(It.IsAny<Registration>(), It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.MarkAsModified(_volunteer), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_FullActivity_ThrowsConflict()
    {
        var activity = TestHelpers.FutureActivity(maxVolunteers: 1, availableSpots: 0);
        SetupActivity(activity);

        await Assert.ThrowsAsync<ConflictException>(() => Register(activity.Id));

        Assert.Equal(0, activity.AvailableSpots);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Register_AlreadyRegistered_ThrowsConflict()
    {
        var activity = TestHelpers.FutureActivity();
        SetupActivity(activity);
        _registrations.Setup(r => r.GetByUserAndActivityForUpdateAsync(_volunteer.Id, activity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Registration { Id = 1, UserId = _volunteer.Id, ActivityId = activity.Id, Status = RegistrationStatus.Active });

        await Assert.ThrowsAsync<ConflictException>(() => Register(activity.Id));

        Assert.Equal(2, activity.AvailableSpots);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Register_CancelledActivity_ThrowsBusinessRule()
    {
        var activity = TestHelpers.FutureActivity();
        activity.IsActive = false;
        SetupActivity(activity);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Register(activity.Id));
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Register_ActivityAlreadyStarted_ThrowsBusinessRule()
    {
        var activity = TestHelpers.FutureActivity();
        activity.StartTime = DateTime.UtcNow.AddMinutes(-30);
        activity.EndTime = DateTime.UtcNow.AddHours(1);
        SetupActivity(activity);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Register(activity.Id));
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Register_OverlappingActivity_ThrowsConflict()
    {
        var activity = TestHelpers.FutureActivity();
        SetupActivity(activity);
        _registrations.Setup(r => r.HasOverlappingActiveRegistrationAsync(
                _volunteer.Id, activity.StartTime, activity.EndTime, activity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => Register(activity.Id));

        Assert.Equal(2, activity.AvailableSpots);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Register_AfterCancellation_ReactivatesSameRow()
    {
        var activity = TestHelpers.FutureActivity();
        SetupActivity(activity);
        var cancelled = new Registration { Id = 5, UserId = _volunteer.Id, ActivityId = activity.Id, Activity = activity, User = _volunteer, Status = RegistrationStatus.Cancelled };
        _registrations.Setup(r => r.GetByUserAndActivityForUpdateAsync(_volunteer.Id, activity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelled);

        var result = await Register(activity.Id);

        Assert.Equal(5, result.Id);
        Assert.Equal(RegistrationStatus.Active, cancelled.Status);
        Assert.Equal(1, activity.AvailableSpots);
        _registrations.Verify(r => r.AddAsync(It.IsAny<Registration>(), It.IsAny<CancellationToken>()), Times.Never);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_ActiveRegistration_SetsCancelled_AndIncrementsSpots()
    {
        var activity = TestHelpers.FutureActivity(availableSpots: 1);
        var registration = new Registration { Id = 3, UserId = _volunteer.Id, User = _volunteer, ActivityId = activity.Id, Activity = activity, Status = RegistrationStatus.Active };
        _registrations.Setup(r => r.GetByIdForUpdateAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(registration);

        await _service.CancelAsync(3, _volunteer.Id, isAdmin: false);

        Assert.Equal(RegistrationStatus.Cancelled, registration.Status);
        Assert.Equal(2, activity.AvailableSpots);
        _users.Verify(u => u.MarkAsModified(_volunteer), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_DoesNotIncrementSpotsAgain()
    {
        var activity = TestHelpers.FutureActivity(availableSpots: 2);
        var registration = new Registration { Id = 3, UserId = _volunteer.Id, User = _volunteer, ActivityId = activity.Id, Activity = activity, Status = RegistrationStatus.Cancelled };
        _registrations.Setup(r => r.GetByIdForUpdateAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(registration);

        await _service.CancelAsync(3, _volunteer.Id, isAdmin: false);

        Assert.Equal(2, activity.AvailableSpots);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Cancel_OtherVolunteersRegistration_ThrowsForbidden()
    {
        var activity = TestHelpers.FutureActivity(availableSpots: 1);
        var registration = new Registration { Id = 3, UserId = 99, ActivityId = activity.Id, Activity = activity, Status = RegistrationStatus.Active };
        _registrations.Setup(r => r.GetByIdForUpdateAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(registration);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CancelAsync(3, _volunteer.Id, isAdmin: false));

        Assert.Equal(RegistrationStatus.Active, registration.Status);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task GetById_OwnRegistration_ReturnsIt()
    {
        var activity = TestHelpers.FutureActivity();
        var registration = new Registration { Id = 4, UserId = _volunteer.Id, User = _volunteer, ActivityId = activity.Id, Activity = activity, Status = RegistrationStatus.Active };
        _registrations.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(registration);

        var result = await _service.GetByIdAsync(4, _volunteer.Id, isAdmin: false);

        Assert.Equal(4, result.Id);
        Assert.Equal(activity.Title, result.ActivityTitle);
    }

    [Fact]
    public async Task GetById_OtherVolunteersRegistration_ThrowsForbidden_ButAdminCanSeeIt()
    {
        var activity = TestHelpers.FutureActivity();
        var registration = new Registration { Id = 4, UserId = 99, ActivityId = activity.Id, Activity = activity, Status = RegistrationStatus.Active };
        _registrations.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(registration);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetByIdAsync(4, _volunteer.Id, isAdmin: false));
        Assert.Equal(4, (await _service.GetByIdAsync(4, currentUserId: 1, isAdmin: true)).Id);
    }

    [Fact]
    public async Task GetById_Unknown_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(404, _volunteer.Id, isAdmin: false));
    }

    [Fact]
    public async Task Register_ConcurrencyConflict_RollsBackAndRethrows()
    {
        var activity = TestHelpers.FutureActivity();
        SetupActivity(activity);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("conflict"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Register(activity.Id));

        _transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
