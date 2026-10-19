using LevBakehila.Core.Entities;
using LevBakehila.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LevBakehila.Data.Repositories;

public class SkillRepository : ISkillRepository
{
    private readonly AppDbContext _context;

    public SkillRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<List<Skill>> GetAllAsync(CancellationToken cancellationToken = default)
        => _context.Skills.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public Task<Skill?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _context.Skills.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Skill?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default)
        => _context.Skills.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<List<Skill>> GetByIdsForUpdateAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
        => _context.Skills.Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken);

    public async Task AddAsync(Skill skill, CancellationToken cancellationToken = default)
        => await _context.Skills.AddAsync(skill, cancellationToken);

    public void Remove(Skill skill) => _context.Skills.Remove(skill);
}
