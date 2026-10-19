using AutoMapper;
using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LevBakehila.Service.Services;

public class SkillService : ISkillService
{
    private readonly ISkillRepository _skills;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<SkillService> _logger;

    public SkillService(ISkillRepository skills, IUnitOfWork unitOfWork, IMapper mapper, ILogger<SkillService> logger)
    {
        _skills = skills;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<List<SkillResponseDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var skills = await _skills.GetAllAsync(cancellationToken);
        return skills.Select(x => _mapper.Map<SkillResponseDto>(x)).ToList();
    }

    public async Task<SkillResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var skill = await _skills.GetByIdAsync(id, cancellationToken) ?? throw SkillNotFound(id);
        return _mapper.Map<SkillResponseDto>(skill);
    }

    public async Task<SkillResponseDto> CreateAsync(CreateSkillDto dto, CancellationToken cancellationToken = default)
    {
        var skill = _mapper.Map<Skill>(dto);
        skill.Name = skill.Name.Trim();

        await _skills.AddAsync(skill, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Skill {SkillId} created", skill.Id);
        return _mapper.Map<SkillResponseDto>(skill);
    }

    public async Task<SkillResponseDto> UpdateAsync(int id, UpdateSkillDto dto, CancellationToken cancellationToken = default)
    {
        var skill = await _skills.GetByIdForUpdateAsync(id, cancellationToken) ?? throw SkillNotFound(id);

        _mapper.Map(dto, skill);
        skill.Name = skill.Name.Trim();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Skill {SkillId} updated", skill.Id);
        return _mapper.Map<SkillResponseDto>(skill);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var skill = await _skills.GetByIdForUpdateAsync(id, cancellationToken) ?? throw SkillNotFound(id);

        // Only the UserSkills / ActivitySkills link rows are removed - no users or activities are lost
        _skills.Remove(skill);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Skill {SkillId} deleted", id);
    }

    private static NotFoundException SkillNotFound(int id) => new($"מיומנות {id} לא נמצאה.");
}
