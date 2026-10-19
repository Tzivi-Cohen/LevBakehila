using AutoMapper;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.DTOs.Users;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LevBakehila.Service.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly ISkillRepository _skills;
    private readonly IRegistrationRepository _registrations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository users,
        ISkillRepository skills,
        IRegistrationRepository registrations,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<UserService> logger)
    {
        _users = users;
        _skills = skills;
        _registrations = registrations;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PagedResult<UserResponseDto>> GetAllAsync(PaginationParams pagination, CancellationToken cancellationToken = default)
    {
        var page = await _users.GetPagedAsync(pagination, cancellationToken);
        return new PagedResult<UserResponseDto>
        {
            Items = page.Items.Select(x => _mapper.Map<UserResponseDto>(x)).ToList(),
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount
        };
    }

    public async Task<UserResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(id, cancellationToken) ?? throw UserNotFound(id);
        return _mapper.Map<UserResponseDto>(user);
    }

    public Task<UserResponseDto> GetProfileAsync(int currentUserId, CancellationToken cancellationToken = default)
        => GetByIdAsync(currentUserId, cancellationToken);

    public async Task<UserResponseDto> UpdateProfileAsync(int currentUserId, UpdateProfileDto dto, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdForUpdateAsync(currentUserId, cancellationToken) ?? throw UserNotFound(currentUserId);

        var skillIds = dto.SkillIds.Distinct().ToList();
        var skills = await _skills.GetByIdsForUpdateAsync(skillIds, cancellationToken);
        if (skills.Count != skillIds.Count)
        {
            throw new BusinessRuleException("אחת או יותר מהמיומנויות שנבחרו אינה קיימת.");
        }

        user.FullName = dto.FullName.Trim();
        user.Neighborhood = dto.Neighborhood.Trim();
        user.Skills.Clear();
        foreach (var skill in skills)
        {
            user.Skills.Add(skill);
        }

        // Updating the user row also changes its Version, so it participates in concurrency checks
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} updated profile", currentUserId);
        return _mapper.Map<UserResponseDto>(user);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdForUpdateAsync(id, cancellationToken) ?? throw UserNotFound(id);

        // Rule 9: never leave inconsistent data - block deletion while active registrations exist
        if (await _registrations.HasActiveByUserAsync(id, cancellationToken))
        {
            throw new ConflictException("לא ניתן למחוק משתמשת שיש לה הרשמות פעילות. יש לבטל את ההרשמות תחילה.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Only cancelled registrations remain; they are removed explicitly (no cascade delete)
            await _registrations.RemoveAllForUserAsync(id, cancellationToken);
            _users.Remove(user);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Logged once, as a Warning, by ExceptionHandlingMiddleware
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation("User {UserId} deleted", id);
    }

    private static NotFoundException UserNotFound(int id) => new($"משתמשת {id} לא נמצאה.");
}
