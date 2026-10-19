using LevBakehila.Core.DTOs.Users;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using LevBakehila.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LevBakehila.Tests.Unit;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISkillRepository> _skills = new();
    private readonly Mock<IRegistrationRepository> _registrations = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITransaction> _transaction = new();
    private readonly UserService _service;

    private readonly User _volunteer = TestHelpers.Volunteer(10);

    public UserServiceTests()
    {
        _unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_transaction.Object);
        _users.Setup(u => u.GetByIdForUpdateAsync(_volunteer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_volunteer);
        _service = new UserService(_users.Object, _skills.Object, _registrations.Object, _unitOfWork.Object,
            TestHelpers.Mapper, NullLogger<UserService>.Instance);
    }

    // ---------- Rule 9 ----------

    [Fact]
    public async Task Delete_UserWithActiveRegistrations_ThrowsConflict_AndDeletesNothing()
    {
        _registrations.Setup(r => r.HasActiveByUserAsync(_volunteer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(_volunteer.Id));

        _users.Verify(u => u.Remove(It.IsAny<User>()), Times.Never);
        _registrations.Verify(r => r.RemoveAllForUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_UserWithoutActiveRegistrations_RemovesHistoryAndUser_InTransaction()
    {
        _registrations.Setup(r => r.HasActiveByUserAsync(_volunteer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _service.DeleteAsync(_volunteer.Id);

        _registrations.Verify(r => r.RemoveAllForUserAsync(_volunteer.Id, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.Remove(_volunteer), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_UnknownUser_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(999));
    }

    [Fact]
    public async Task Delete_ConcurrencyConflict_RollsBackAndRethrows()
    {
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("conflict"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _service.DeleteAsync(_volunteer.Id));

        _transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Profile ----------

    [Fact]
    public async Task UpdateProfile_UpdatesNameNeighborhoodAndSkills_ButNeverTheRole()
    {
        var skills = new List<Skill> { new() { Id = 1, Name = "Music" }, new() { Id = 2, Name = "Sport" } };
        _skills.Setup(s => s.GetByIdsForUpdateAsync(It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(skills);

        var result = await _service.UpdateProfileAsync(_volunteer.Id, new UpdateProfileDto
        {
            FullName = "  New Name ",
            Neighborhood = " Gilo ",
            SkillIds = new List<int> { 1, 2, 2 }
        });

        Assert.Equal("New Name", result.FullName);
        Assert.Equal("Gilo", result.Neighborhood);
        Assert.Equal(new[] { 1, 2 }, result.Skills.Select(s => s.Id).OrderBy(id => id));
        Assert.Equal(UserRole.Volunteer, _volunteer.Role);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProfile_UnknownSkill_ThrowsBusinessRule_AndSavesNothing()
    {
        _skills.Setup(s => s.GetByIdsForUpdateAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Skill>());

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateProfileAsync(_volunteer.Id,
            new UpdateProfileDto { FullName = "Name", Neighborhood = "Ramot", SkillIds = new List<int> { 77 } }));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProfile_UnknownUser_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetProfileAsync(999));
    }
}
