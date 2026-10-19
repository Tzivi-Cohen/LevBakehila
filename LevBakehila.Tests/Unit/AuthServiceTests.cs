using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LevBakehila.Core.DTOs.Auth;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using LevBakehila.Core.Settings;
using LevBakehila.Service.Security;
using LevBakehila.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LevBakehila.Tests.Unit;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly PasswordHasher _passwordHasher = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        var jwtSettings = Options.Create(new JwtSettings
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Key = "unit-test-signing-key-with-at-least-32-chars",
            ExpiryMinutes = 60
        });

        _service = new AuthService(_users.Object, _unitOfWork.Object, _passwordHasher,
            jwtSettings, TestHelpers.Mapper, NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task Register_ValidRequest_CreatesVolunteerWithHashedPassword()
    {
        User? savedUser = null;
        _users.Setup(u => u.EmailExistsAsync("dana@test.local", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => savedUser = user);

        var result = await _service.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Dana",
            Email = " Dana@Test.local ",
            Password = "Secret123!",
            Neighborhood = "Ramot"
        });

        Assert.NotNull(savedUser);
        Assert.Equal(UserRole.Volunteer, savedUser!.Role);
        Assert.Equal("dana@test.local", savedUser.Email);
        Assert.NotEqual("Secret123!", savedUser.PasswordHash);
        Assert.True(_passwordHasher.Verify("Secret123!", savedUser.PasswordHash));
        Assert.Equal(UserRole.Volunteer, result.Role);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_ExistingEmail_ThrowsConflict()
    {
        _users.Setup(u => u.EmailExistsAsync("dana@test.local", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _service.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Dana",
            Email = "dana@test.local",
            Password = "Secret123!",
            Neighborhood = "Ramot"
        }));

        _users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsJwtWithUserIdAndRole()
    {
        var user = TestHelpers.Volunteer(7);
        user.PasswordHash = _passwordHasher.Hash("Secret123!");
        _users.Setup(u => u.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _service.LoginAsync(new LoginRequestDto { Email = user.Email, Password = "Secret123!" });

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(7, result.UserId);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal("test-issuer", token.Issuer);
        Assert.Contains(token.Claims, c => c.Type == "role" && c.Value == "Volunteer");
        Assert.Contains(token.Claims, c => c.Type == "nameid" && c.Value == "7");
        Assert.DoesNotContain(token.Claims, c => c.Value.Contains("Secret123!"));
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsUnauthorized()
    {
        var user = TestHelpers.Volunteer(7);
        user.PasswordHash = _passwordHasher.Hash("Secret123!");
        _users.Setup(u => u.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequestDto { Email = user.Email, Password = "WrongPassword!" }));
    }
}
