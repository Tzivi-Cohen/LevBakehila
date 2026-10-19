using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LevBakehila.Data.Repositories;

public class RegistrationRepository : IRegistrationRepository
{
    private readonly AppDbContext _context;

    public RegistrationRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<Registration?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default)
        => _context.Registrations
            .Include(r => r.Activity)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Registration?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _context.Registrations
            .AsNoTracking()
            .Include(r => r.Activity)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Registration?> GetByUserAndActivityForUpdateAsync(int userId, int activityId, CancellationToken cancellationToken = default)
        => _context.Registrations
            .FirstOrDefaultAsync(r => r.UserId == userId && r.ActivityId == activityId, cancellationToken);

    public Task<List<Registration>> GetByUserAsync(int userId, CancellationToken cancellationToken = default)
        => _context.Registrations
            .AsNoTracking()
            .Include(r => r.Activity)
            .Include(r => r.User)
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.Activity.StartTime)
            .ToListAsync(cancellationToken);

    public Task<List<Registration>> GetByActivityAsync(int activityId, CancellationToken cancellationToken = default)
        => _context.Registrations
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Activity)
            .Where(r => r.ActivityId == activityId)
            .OrderBy(r => r.User.FullName)
            .ToListAsync(cancellationToken);

    public Task<bool> IsActiveRegistrationAsync(int userId, int activityId, CancellationToken cancellationToken = default)
        => _context.Registrations.AnyAsync(
            r => r.UserId == userId && r.ActivityId == activityId && r.Status == RegistrationStatus.Active,
            cancellationToken);

    public Task<bool> HasActiveByUserAsync(int userId, CancellationToken cancellationToken = default)
        => _context.Registrations.AnyAsync(
            r => r.UserId == userId && r.Status == RegistrationStatus.Active,
            cancellationToken);

    public Task<bool> HasOverlappingActiveRegistrationAsync(int userId, DateTime startTime, DateTime endTime, int excludeActivityId, CancellationToken cancellationToken = default)
        => _context.Registrations.AnyAsync(
            r => r.UserId == userId
                 && r.Status == RegistrationStatus.Active
                 && r.ActivityId != excludeActivityId
                 && r.Activity.StartTime < endTime
                 && startTime < r.Activity.EndTime,
            cancellationToken);

    public Task<bool> HasOverlapForRegisteredVolunteersAsync(int activityId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default)
    {
        // One SQL query (sub-select) instead of one query per volunteer - avoids N+1
        var registeredUserIds = _context.Registrations
            .Where(r => r.ActivityId == activityId && r.Status == RegistrationStatus.Active)
            .Select(r => r.UserId);

        return _context.Registrations.AnyAsync(
            r => registeredUserIds.Contains(r.UserId)
                 && r.ActivityId != activityId
                 && r.Status == RegistrationStatus.Active
                 && r.Activity.StartTime < endTime
                 && startTime < r.Activity.EndTime,
            cancellationToken);
    }

    public async Task AddAsync(Registration registration, CancellationToken cancellationToken = default)
        => await _context.Registrations.AddAsync(registration, cancellationToken);

    public async Task RemoveAllForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var registrations = await _context.Registrations
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken);
        _context.Registrations.RemoveRange(registrations);
    }
}
