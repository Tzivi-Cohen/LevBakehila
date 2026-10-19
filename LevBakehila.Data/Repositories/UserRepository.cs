using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LevBakehila.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Skills)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _context.Users
            .AsNoTracking()
            .Include(u => u.Skills)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        => _context.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public Task<PagedResult<User>> GetPagedAsync(PaginationParams pagination, CancellationToken cancellationToken = default)
    {
        var query = _context.Users.AsNoTracking().Include(u => u.Skills);
        bool desc = pagination.IsDescending;

        IOrderedQueryable<User> ordered = pagination.SortBy?.ToLowerInvariant() switch
        {
            "email" => desc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "neighborhood" => desc ? query.OrderByDescending(u => u.Neighborhood) : query.OrderBy(u => u.Neighborhood),
            _ => desc ? query.OrderByDescending(u => u.FullName) : query.OrderBy(u => u.FullName)
        };

        return ordered.ThenBy(u => u.Id)
            .ToPagedResultAsync(pagination.PageNumber, pagination.PageSize, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        => await _context.Users.AddAsync(user, cancellationToken);

    public void Remove(User user) => _context.Users.Remove(user);

    public void MarkAsModified(User user)
    {
        // Marking a column as modified makes EF send a real
        // "UPDATE Users SET ... WHERE Id = @id AND Version = @originalVersion".
        // PostgreSQL then generates a new xmin (Version), and a parallel request that read the
        // old Version fails with DbUpdateConcurrencyException.
        _context.Entry(user).Property(u => u.FullName).IsModified = true;
    }
}
