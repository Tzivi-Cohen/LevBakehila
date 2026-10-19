using AutoMapper;
using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LevBakehila.Service.Services;

public class ActivityService : IActivityService
{
    private readonly IActivityRepository _activities;
    private readonly IRegistrationRepository _registrations;
    private readonly IUserRepository _users;
    private readonly ISkillRepository _skills;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(
        IActivityRepository activities,
        IRegistrationRepository registrations,
        IUserRepository users,
        ISkillRepository skills,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<ActivityService> logger)
    {
        _activities = activities;
        _registrations = registrations;
        _users = users;
        _skills = skills;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PagedResult<ActivityResponseDto>> GetAllAsync(ActivityFilterDto filter, int currentUserId, CancellationToken cancellationToken = default)
    {
        if (filter.OnlyAvailable)
        {
            return await GetAvailableActivitiesAsync(filter, currentUserId, cancellationToken);
        }

        var page = await _activities.GetPagedAsync(filter, availableFrom: null,
            await PreferredNeighborhoodAsync(filter, currentUserId, cancellationToken), cancellationToken);
        return ToDtoPage(page);
    }

    public async Task<PagedResult<ActivityResponseDto>> GetAvailableActivitiesAsync(ActivityFilterDto filter, int currentUserId, CancellationToken cancellationToken = default)
    {
        var page = await _activities.GetPagedAsync(filter, availableFrom: DateTime.UtcNow,
            await PreferredNeighborhoodAsync(filter, currentUserId, cancellationToken), cancellationToken);
        return ToDtoPage(page);
    }

    // Rule 5: the home neighborhood comes from the volunteer's profile, never from the request
    private async Task<string?> PreferredNeighborhoodAsync(ActivityFilterDto filter, int currentUserId, CancellationToken cancellationToken)
    {
        if (!filter.MyNeighborhoodFirst)
        {
            return null;
        }

        var user = await _users.GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new NotFoundException($"משתמשת {currentUserId} לא נמצאה.");
        return user.Neighborhood;
    }

    public async Task<ActivityResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var activity = await _activities.GetByIdAsync(id, cancellationToken) ?? throw ActivityNotFound(id);
        return _mapper.Map<ActivityResponseDto>(activity);
    }

    public async Task<ActivityResponseDto> CreateAsync(CreateActivityDto dto, CancellationToken cancellationToken = default)
    {
        ValidateTimes(dto.StartTime, dto.EndTime);

        var activity = _mapper.Map<VolunteerActivity>(dto);
        activity.AvailableSpots = activity.MaxVolunteers;
        activity.IsActive = true;
        await SetRequiredSkillsAsync(activity, dto.RequiredSkillIds, cancellationToken);

        await _activities.AddAsync(activity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Activity {ActivityId} created", activity.Id);
        return _mapper.Map<ActivityResponseDto>(activity);
    }

    // Rule 6 under concurrency: the activity row (RowVersion) and, when the hours change, the row of every
    // registered volunteer (Version) are updated in one transaction. A parallel registration of one of those
    // volunteers to another activity updates the same user row, so only one of the two requests can succeed.
    public async Task<ActivityResponseDto> UpdateAsync(int id, UpdateActivityDto dto, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Volunteers are read here, before the overlap check, so the Version they hold is the one that was checked
            var activity = await _activities.GetByIdWithActiveRegistrationsForUpdateAsync(id, cancellationToken) ?? throw ActivityNotFound(id);

            if (!activity.IsActive)
            {
                throw new BusinessRuleException("לא ניתן לערוך פעילות שבוטלה.");
            }

            ValidateTimes(dto.StartTime, dto.EndTime);

            // Rule 6: the quota can never drop below the number of active registrations
            var activeRegistrations = ActiveRegistrations(activity);
            if (dto.MaxVolunteers < activeRegistrations.Count)
            {
                throw new ConflictException($"לא ניתן להקטין את המכסה מתחת למספר הנרשמות הפעילות ({activeRegistrations.Count}).");
            }

            // Rule 6: new hours must not create an overlap for the volunteers already registered
            bool timesChanged = dto.StartTime != activity.StartTime || dto.EndTime != activity.EndTime;
            if (timesChanged && activeRegistrations.Count > 0)
            {
                if (await _registrations.HasOverlapForRegisteredVolunteersAsync(id, dto.StartTime, dto.EndTime, cancellationToken))
                {
                    throw new ConflictException("שינוי השעות יוצר חפיפה עם הרשמה אחרת של אחת המתנדבות הרשומות.");
                }

                // The check above is not enough on its own - a real UPDATE of each volunteer's row joins the race
                foreach (var registration in activeRegistrations)
                {
                    _users.MarkAsModified(registration.User);
                }
            }

            _mapper.Map(dto, activity);
            activity.AvailableSpots = activity.MaxVolunteers - activeRegistrations.Count;
            await SetRequiredSkillsAsync(activity, dto.RequiredSkillIds, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Activity {ActivityId} updated", id);
            return _mapper.Map<ActivityResponseDto>(activity);
        }
        catch (ConcurrencyConflictException)
        {
            // Logged once, as a Warning, by ExceptionHandlingMiddleware
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CancelActivityAsync(int id, CancellationToken cancellationToken = default)
    {
        // Rule 8: no physical delete - cancel the activity and all its active registrations in one transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var activity = await _activities.GetByIdWithActiveRegistrationsForUpdateAsync(id, cancellationToken) ?? throw ActivityNotFound(id);
            if (!activity.IsActive)
            {
                return;
            }

            var activeRegistrations = ActiveRegistrations(activity);
            foreach (var registration in activeRegistrations)
            {
                registration.Status = RegistrationStatus.Cancelled;
                _users.MarkAsModified(registration.User);
            }

            activity.IsActive = false;
            activity.AvailableSpots = activity.MaxVolunteers;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Activity {ActivityId} cancelled together with {Count} registrations", id, activeRegistrations.Count);
        }
        catch (ConcurrencyConflictException)
        {
            // Logged once, as a Warning, by ExceptionHandlingMiddleware
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static List<Registration> ActiveRegistrations(VolunteerActivity activity)
        => activity.Registrations.Where(r => r.Status == RegistrationStatus.Active).ToList();

    private async Task SetRequiredSkillsAsync(VolunteerActivity activity, List<int> requiredSkillIds, CancellationToken cancellationToken)
    {
        var skillIds = requiredSkillIds.Distinct().ToList();
        var skills = await _skills.GetByIdsForUpdateAsync(skillIds, cancellationToken);
        if (skills.Count != skillIds.Count)
        {
            throw new BusinessRuleException("אחת או יותר מהמיומנויות שנבחרו אינה קיימת.");
        }

        activity.RequiredSkills.Clear();
        foreach (var skill in skills)
        {
            activity.RequiredSkills.Add(skill);
        }
    }

    private static void ValidateTimes(DateTime startTime, DateTime endTime)
    {
        if (endTime <= startTime)
        {
            throw new BusinessRuleException("שעת הסיום חייבת להיות אחרי שעת ההתחלה.");
        }

        if (startTime <= DateTime.UtcNow)
        {
            throw new BusinessRuleException("מועד תחילת הפעילות חייב להיות בעתיד.");
        }
    }

    private PagedResult<ActivityResponseDto> ToDtoPage(PagedResult<VolunteerActivity> page) => new()
    {
        Items = page.Items.Select(x => _mapper.Map<ActivityResponseDto>(x)).ToList(),
        PageNumber = page.PageNumber,
        PageSize = page.PageSize,
        TotalCount = page.TotalCount
    };

    private static NotFoundException ActivityNotFound(int id) => new($"פעילות {id} לא נמצאה.");
}
