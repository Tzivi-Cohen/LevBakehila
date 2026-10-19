using AutoMapper;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LevBakehila.Core.DTOs.Auth;
using LevBakehila.Core.DTOs.Users;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using LevBakehila.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LevBakehila.Service.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly JwtSettings _jwtSettings;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IOptions<JwtSettings> jwtOptions,
        IMapper mapper,
        ILogger<AuthService> logger)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtSettings = jwtOptions.Value;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<UserResponseDto> RegisterAsync(RegisterRequestDto dto, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(dto.Email);

        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            throw new ConflictException("כבר קיים חשבון עם כתובת האימייל הזו.");
        }

        // A new user is ALWAYS a Volunteer - the role is never taken from the request
        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            Neighborhood = dto.Neighborhood.Trim(),
            Role = UserRole.Volunteer,
            PasswordHash = _passwordHasher.Hash(dto.Password)
        };

        await _users.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New volunteer registered. UserId: {UserId}", user.Id);
        return _mapper.Map<UserResponseDto>(user);
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(NormalizeEmail(dto.Email), cancellationToken);

        // Same message for "no such user" and "wrong password" - do not reveal which emails exist
        // The rejected login is logged once, as a Warning, by ExceptionHandlingMiddleware (without the email)
        if (user is null || !_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("אימייל או סיסמה שגויים.");
        }

        _logger.LogInformation("User {UserId} logged in", user.Id);

        return new LoginResponseDto
        {
            Token = GenerateJwtToken(user),
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
            UserId = user.Id,
            FullName = user.FullName,
            Role = user.Role
        };
    }

    public string GenerateJwtToken(User user)
    {
        // Only what the server needs to identify and authorize the user - no password, no personal data
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // CreateToken applies the outbound claim map, so the token carries the standard short names
        // (nameid, role, ...) and JwtBearer maps them back to ClaimTypes.* on the way in
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
