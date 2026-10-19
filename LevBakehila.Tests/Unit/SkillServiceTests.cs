using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.Entities;
using LevBakehila.Core.Exceptions;
using LevBakehila.Core.Interfaces;
using LevBakehila.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LevBakehila.Tests.Unit;

public class SkillServiceTests
{
    private readonly Mock<ISkillRepository> _skills = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly SkillService _service;

    public SkillServiceTests()
    {
        _service = new SkillService(_skills.Object, _unitOfWork.Object, TestHelpers.Mapper, NullLogger<SkillService>.Instance);
    }

    [Fact]
    public async Task Create_TrimsName_AndSaves()
    {
        Skill? added = null;
        _skills.Setup(s => s.AddAsync(It.IsAny<Skill>(), It.IsAny<CancellationToken>()))
            .Callback<Skill, CancellationToken>((skill, _) => added = skill);

        var result = await _service.CreateAsync(new CreateSkillDto { Name = "  Music  " });

        Assert.Equal("Music", added!.Name);
        Assert.Equal("Music", result.Name);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_UnknownSkill_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(5, new UpdateSkillDto { Name = "Sport" }));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_ExistingSkill_RemovesOnlyTheSkill()
    {
        // The database removes the UserSkills / ActivitySkills link rows (cascade on the link tables only);
        // this is proven against PostgreSQL in ActivityIntegrationTests
        var skill = new Skill { Id = 5, Name = "Music" };
        _skills.Setup(s => s.GetByIdForUpdateAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(skill);

        await _service.DeleteAsync(5);

        _skills.Verify(s => s.Remove(skill), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_UnknownSkill_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(5));
        _skills.Verify(s => s.Remove(It.IsAny<Skill>()), Times.Never);
    }
}
