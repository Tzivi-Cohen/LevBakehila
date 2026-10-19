using AutoMapper;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LevBakehila.Service.Services;

public class RegistrationService : IRegistrationService
{
    private readonly IRegistrationRepository _registrations;
    private readonly IActivityRepository _activities;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<RegistrationService> _logger;

    public RegistrationService(
        IRegistrationRepository registrations,
        IActivityRepository activities,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<RegistrationService> logger)
    {
        _registrations = registrations;
        _activities = activities;
        _users = users;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    // Registration flow (PLAN 7.4):
    // transaction -> read activity + user -> business checks -> create/reactivate ->
    // AvailableSpots-1 -> real update of the user row (Version) -> SaveChanges -> Commit.
    // If another request changed the activity (RowVersion) or the user (Version) in the meantime,
    // SaveChanges fails, we roll back and the API returns 409.
    public async Task<RegistrationResponseDto> RegisterAsync(int currentUserId, CreateRegistrationDto dto, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var activity = await _activities.GetByIdForUpdateAsync(dto.ActivityId, cancellationToken)
                ?? throw new NotFoundException($"פעילות {dto.ActivityId} לא נמצאה.");
            var user = await _users.GetByIdForUpdateAsync(currentUserId, cancellationToken)
                ?? throw new NotFoundException($"משתמשת {currentUserId} לא נמצאה.");

            // Rule 1
            if (!activity.IsActive)
            {
                throw new BusinessRuleException("הפעילות בוטלה ולא ניתן להירשם אליה.");
            }
            if (activity.StartTime <= DateTime.UtcNow)
            {
                throw new BusinessRuleException("הפעילות כבר התחילה או הסתיימה.");
            }

            // Rule 3
            var existing = await _registrations.GetByUserAndActivityForUpdateAsync(currentUserId, activity.Id, cancellationToken);
            if (existing is { Status: RegistrationStatus.Active })
            {
                throw new ConflictException("את כבר רשומה לפעילות הזו.");
            }

            // Rule 2
            if (activity.AvailableSpots <= 0)
            {
                throw new ConflictException("אין מקומות פנויים בפעילות הזו.");
            }

            // Rule 4
            if (await HasTimeConflictAsync(currentUserId, activity.StartTime, activity.EndTime, activity.Id, cancellationToken))
            {
                throw new ConflictException("את כבר רשומה לפעילות אחרת בשעות חופפות.");
            }

            Registration registration;
            if (existing is not null)
            {
                // Re-activate the same row - never a second row for the same volunteer + activity
                registration = existing;
                registration.Status = RegistrationStatus.Active;
                registration.RegisteredAt = DateTime.UtcNow;
            }
            else
            {
                registration = new Registration
                {
                    UserId = user.Id,
                    User = user,
                    ActivityId = activity.Id,
                    Activity = activity,
                    RegisteredAt = DateTime.UtcNow,
                    Status = RegistrationStatus.Active
                };
                await _registrations.AddAsync(registration, cancellationToken);
            }

            activity.AvailableSpots--;
            _users.MarkAsModified(user);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("User {UserId} registered to activity {ActivityId}", user.Id, activity.Id);
            return _mapper.Map<RegistrationResponseDto>(registration);
        }
        catch (ConcurrencyConflictException)
        {
            // Logged once, as a Warning, by ExceptionHandlingMiddleware
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CancelAsync(int registrationId, int currentUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var registration = await _registrations.GetByIdForUpdateAsync(registrationId, cancellationToken)
                ?? throw new NotFoundException($"הרשמה {registrationId} לא נמצאה.");

            // Rule 7: only the volunteer herself or the coordinator
            if (!isAdmin && registration.UserId != currentUserId)
            {
                throw new ForbiddenException("אין לך הרשאה לבטל הרשמה זו.");
            }

            // Rule 7: cancelling twice must not add a spot twice
            if (registration.Status == RegistrationStatus.Cancelled)
            {
                return;
            }

            // Status change and quota update are saved together (PLAN 7.6)
            registration.Status = RegistrationStatus.Cancelled;
            if (registration.Activity.AvailableSpots < registration.Activity.MaxVolunteers)
            {
                registration.Activity.AvailableSpots++;
            }
            _users.MarkAsModified(registration.User);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Registration {RegistrationId} cancelled by user {UserId}", registrationId, currentUserId);
        }
        catch (ConcurrencyConflictException)
        {
            // Logged once, as a Warning, by ExceptionHandlingMiddleware
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<RegistrationResponseDto> GetByIdAsync(int registrationId, int currentUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var registration = await _registrations.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new NotFoundException($"הרשמה {registrationId} לא נמצאה.");

        // Same rule as cancelling: only the volunteer herself or the coordinator
        if (!isAdmin && registration.UserId != currentUserId)
        {
            throw new ForbiddenException("אין לך הרשאה לצפות בהרשמה זו.");
        }

        return _mapper.Map<RegistrationResponseDto>(registration);
    }

    public async Task<List<RegistrationResponseDto>> GetMyRegistrationsAsync(int currentUserId, CancellationToken cancellationToken = default)
    {
        var registrations = await _registrations.GetByUserAsync(currentUserId, cancellationToken);
        return registrations.Select(x => _mapper.Map<RegistrationResponseDto>(x)).ToList();
    }

    public async Task<List<RegistrationResponseDto>> GetRegistrationsByActivityAsync(int activityId, CancellationToken cancellationToken = default)
    {
        _ = await _activities.GetByIdAsync(activityId, cancellationToken)
            ?? throw new NotFoundException($"פעילות {activityId} לא נמצאה.");

        var registrations = await _registrations.GetByActivityAsync(activityId, cancellationToken);
        return registrations.Select(x => _mapper.Map<RegistrationResponseDto>(x)).ToList();
    }

    public Task<bool> HasTimeConflictAsync(int userId, DateTime startTime, DateTime endTime, int excludeActivityId, CancellationToken cancellationToken = default)
        => _registrations.HasOverlappingActiveRegistrationAsync(userId, startTime, endTime, excludeActivityId, cancellationToken);

    public Task<bool> IsAlreadyRegisteredAsync(int userId, int activityId, CancellationToken cancellationToken = default)
        => _registrations.IsActiveRegistrationAsync(userId, activityId, cancellationToken);

    public async Task<bool> HasAvailableSpotsAsync(int activityId, CancellationToken cancellationToken = default)
    {
        var activity = await _activities.GetByIdAsync(activityId, cancellationToken)
            ?? throw new NotFoundException($"פעילות {activityId} לא נמצאה.");
        return activity.AvailableSpots > 0;
    }
}
