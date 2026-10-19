using LevBakehila.Core.DTOs.Skills;
using LevBakehila.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LevBakehila.API.Controllers;

[ApiController]
[Route("api/skills")]
[Authorize]
[Produces("application/json")]
public class SkillsController : ControllerBase
{
    private readonly ISkillService _skillService;

    public SkillsController(ISkillService skillService)
    {
        _skillService = skillService;
    }

    /// <summary>Lists all skills.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<SkillResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SkillResponseDto>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _skillService.GetAllAsync(cancellationToken));
    }

    /// <summary>Gets one skill.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(SkillResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SkillResponseDto>> GetById([FromRoute] int id, CancellationToken cancellationToken)
    {
        return Ok(await _skillService.GetByIdAsync(id, cancellationToken));
    }

    /// <summary>Creates a skill (Admin only).</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SkillResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SkillResponseDto>> Create([FromBody] CreateSkillDto dto, CancellationToken cancellationToken)
    {
        var skill = await _skillService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = skill.Id }, skill);
    }

    /// <summary>Updates a skill (Admin only).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SkillResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SkillResponseDto>> Update([FromRoute] int id, [FromBody] UpdateSkillDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _skillService.UpdateAsync(id, dto, cancellationToken));
    }

    /// <summary>Deletes a skill (Admin only).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        await _skillService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
