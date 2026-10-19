using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.Entities;

namespace LevBakehila.Core.Interfaces;

// Repositories = data access only. No business decisions here.
// "ForUpdate" methods return tracked entities; all other reads use AsNoTracking.

public interface IUserRepository
{
    Task<User?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task<PagedResult<User>> GetPagedAsync(PaginationParams pagination, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    void Remove(User user);

    // Forces a real UPDATE of the user row so PostgreSQL changes its Version (xmin)
    void MarkAsModified(User user);
}

public interface IActivityRepository
{
    Task<VolunteerActivity?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default);
    Task<VolunteerActivity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    // Tracked activity + its required skills + its ACTIVE registrations with their volunteers, in one load
    Task<VolunteerActivity?> GetByIdWithActiveRegistrationsForUpdateAsync(int id, CancellationToken cancellationToken = default);

    // availableFrom != null => only active activities that start after it and still have free spots
    // preferredNeighborhood != null => activities in that neighborhood are ordered first
    Task<PagedResult<VolunteerActivity>> GetPagedAsync(ActivityFilterDto filter, DateTime? availableFrom, string? preferredNeighborhood, CancellationToken cancellationToken = default);
    Task AddAsync(VolunteerActivity activity, CancellationToken cancellationToken = default);
}

public interface IRegistrationRepository
{
    Task<Registration?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default);
    Task<Registration?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Registration?> GetByUserAndActivityForUpdateAsync(int userId, int activityId, CancellationToken cancellationToken = default);
    Task<List<Registration>> GetByUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<Registration>> GetByActivityAsync(int activityId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveRegistrationAsync(int userId, int activityId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveByUserAsync(int userId, CancellationToken cancellationToken = default);

    // Overlap rule: existing.StartTime < newEnd && newStart < existing.EndTime
    Task<bool> HasOverlappingActiveRegistrationAsync(int userId, DateTime startTime, DateTime endTime, int excludeActivityId, CancellationToken cancellationToken = default);

    // Rule 6: would moving activityId to the new hours overlap another active registration of any of its volunteers?
    Task<bool> HasOverlapForRegisteredVolunteersAsync(int activityId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default);

    Task AddAsync(Registration registration, CancellationToken cancellationToken = default);
    Task RemoveAllForUserAsync(int userId, CancellationToken cancellationToken = default);
}

public interface ISkillRepository
{
    Task<List<Skill>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Skill?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Skill?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default);
    Task<List<Skill>> GetByIdsForUpdateAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
    Task AddAsync(Skill skill, CancellationToken cancellationToken = default);
    void Remove(Skill skill);
}
