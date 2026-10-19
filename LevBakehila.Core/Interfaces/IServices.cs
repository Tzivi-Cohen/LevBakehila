using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Auth;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.DTOs.Users;
using LevBakehila.Core.Entities;

namespace LevBakehila.Core.Interfaces;

public interface IAuthService
{
    Task<UserResponseDto> RegisterAsync(RegisterRequestDto dto, CancellationToken cancellationToken = default);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken cancellationToken = default);
    string GenerateJwtToken(User user);
}

public interface IUserService
{
    Task<PagedResult<UserResponseDto>> GetAllAsync(PaginationParams pagination, CancellationToken cancellationToken = default);
    Task<UserResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<UserResponseDto> GetProfileAsync(int currentUserId, CancellationToken cancellationToken = default);
    Task<UserResponseDto> UpdateProfileAsync(int currentUserId, UpdateProfileDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public interface IActivityService
{
    // currentUserId is needed for MyNeighborhoodFirst (the volunteer's home neighborhood)
    Task<PagedResult<ActivityResponseDto>> GetAllAsync(ActivityFilterDto filter, int currentUserId, CancellationToken cancellationToken = default);
    Task<PagedResult<ActivityResponseDto>> GetAvailableActivitiesAsync(ActivityFilterDto filter, int currentUserId, CancellationToken cancellationToken = default);
    Task<ActivityResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ActivityResponseDto> CreateAsync(CreateActivityDto dto, CancellationToken cancellationToken = default);
    Task<ActivityResponseDto> UpdateAsync(int id, UpdateActivityDto dto, CancellationToken cancellationToken = default);

    // Rule 8: no physical delete - IsActive = false and all active registrations are cancelled, in one transaction
    Task CancelActivityAsync(int id, CancellationToken cancellationToken = default);
}

public interface IRegistrationService
{
    Task<RegistrationResponseDto> RegisterAsync(int currentUserId, CreateRegistrationDto dto, CancellationToken cancellationToken = default);
    Task CancelAsync(int registrationId, int currentUserId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<RegistrationResponseDto> GetByIdAsync(int registrationId, int currentUserId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<List<RegistrationResponseDto>> GetMyRegistrationsAsync(int currentUserId, CancellationToken cancellationToken = default);
    Task<List<RegistrationResponseDto>> GetRegistrationsByActivityAsync(int activityId, CancellationToken cancellationToken = default);
    Task<bool> HasTimeConflictAsync(int userId, DateTime startTime, DateTime endTime, int excludeActivityId, CancellationToken cancellationToken = default);
    Task<bool> IsAlreadyRegisteredAsync(int userId, int activityId, CancellationToken cancellationToken = default);
    Task<bool> HasAvailableSpotsAsync(int activityId, CancellationToken cancellationToken = default);
}

public interface ISkillService
{
    Task<List<SkillResponseDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SkillResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<SkillResponseDto> CreateAsync(CreateSkillDto dto, CancellationToken cancellationToken = default);
    Task<SkillResponseDto> UpdateAsync(int id, UpdateSkillDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
