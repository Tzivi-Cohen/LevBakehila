using System.Linq.Expressions;
using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LevBakehila.Data.Repositories;

public class ActivityRepository : IActivityRepository
{
    private readonly AppDbContext _context;

    public ActivityRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<VolunteerActivity?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default)
        => _context.Activities.Include(a => a.RequiredSkills).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<VolunteerActivity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _context.Activities.AsNoTracking().Include(a => a.RequiredSkills).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<VolunteerActivity?> GetByIdWithActiveRegistrationsForUpdateAsync(int id, CancellationToken cancellationToken = default)
        => _context.Activities
            .Include(a => a.RequiredSkills)
            .Include(a => a.Registrations.Where(r => r.Status == RegistrationStatus.Active))
                .ThenInclude(r => r.User)
            // Two collection includes - split into separate SQL queries instead of one cartesian join
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<PagedResult<VolunteerActivity>> GetPagedAsync(ActivityFilterDto filter, DateTime? availableFrom, string? preferredNeighborhood, CancellationToken cancellationToken = default)
    {
        IQueryable<VolunteerActivity> query = _context.Activities.AsNoTracking().Include(a => a.RequiredSkills);

        if (!string.IsNullOrWhiteSpace(filter.Neighborhood))
        {
            string neighborhood = filter.Neighborhood.Trim();
            query = query.Where(a => a.Neighborhood == neighborhood);
        }

        if (filter.Category.HasValue)
        {
            var category = filter.Category.Value;
            query = query.Where(a => a.Category == category);
        }

        if (availableFrom.HasValue)
        {
            var from = availableFrom.Value;
            query = query.Where(a => a.IsActive && a.StartTime > from && a.AvailableSpots > 0);
        }

        // "Close to home" first (ORDER BY CASE ... in SQL), then the requested sort - all before Skip/Take
        IOrderedQueryable<VolunteerActivity>? homeFirst = preferredNeighborhood is null
            ? null
            : query.OrderBy(a => a.Neighborhood == preferredNeighborhood ? 0 : 1);

        bool desc = filter.IsDescending;
        IOrderedQueryable<VolunteerActivity> Sort<TKey>(Expression<Func<VolunteerActivity, TKey>> key) => homeFirst is null
            ? (desc ? query.OrderByDescending(key) : query.OrderBy(key))
            : (desc ? homeFirst.ThenByDescending(key) : homeFirst.ThenBy(key));

        IOrderedQueryable<VolunteerActivity> ordered = filter.SortBy?.ToLowerInvariant() switch
        {
            "title" => Sort(a => a.Title),
            "neighborhood" => Sort(a => a.Neighborhood),
            "availablespots" => Sort(a => a.AvailableSpots),
            _ => Sort(a => a.StartTime)
        };

        return ordered.ThenBy(a => a.Id)
            .ToPagedResultAsync(filter.PageNumber, filter.PageSize, cancellationToken);
    }

    public async Task AddAsync(VolunteerActivity activity, CancellationToken cancellationToken = default)
        => await _context.Activities.AddAsync(activity, cancellationToken);
}
