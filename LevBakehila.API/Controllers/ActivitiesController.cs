using LevBakehila.API.Extensions;
using LevBakehila.Core.DTOs.Activities;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LevBakehila.API.Controllers;

[ApiController]
[Route("api/activities")]
[Authorize]
[Produces("application/json")]
public class ActivitiesController : ControllerBase
{
    private readonly IActivityService _activityService;
    private readonly IRegistrationService _registrationService;

    public ActivitiesController(IActivityService activityService, IRegistrationService registrationService)
    {
        _activityService = activityService;
        _registrationService = registrationService;
    }

    /// <summary>
    /// Lists activities with filtering (neighborhood, category, onlyAvailable), sorting and paging.
    /// SortBy: startTime | title | neighborhood | availableSpots.
    /// myNeighborhoodFirst=true puts activities in the logged-in user's home neighborhood first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ActivityResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ActivityResponseDto>>> GetAll([FromQuery] ActivityFilterDto filter, CancellationToken cancellationToken)
    {
        return Ok(await _activityService.GetAllAsync(filter, User.GetUserId(), cancellationToken));
    }

    /// <summary>Gets one activity, including the number of free spots.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ActivityResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityResponseDto>> GetById([FromRoute] int id, CancellationToken cancellationToken)
    {
        return Ok(await _activityService.GetByIdAsync(id, cancellationToken));
    }

    /// <summary>Lists the volunteers registered to an activity, active and cancelled (Admin only).</summary>
    [HttpGet("{id:int}/registrations")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(List<RegistrationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<RegistrationResponseDto>>> GetRegistrations([FromRoute] int id, CancellationToken cancellationToken)
    {
        return Ok(await _registrationService.GetRegistrationsByActivityAsync(id, cancellationToken));
    }

    /// <summary>Creates a volunteer activity (Admin only).</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ActivityResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ActivityResponseDto>> Create([FromBody] CreateActivityDto dto, CancellationToken cancellationToken)
    {
        var activity = await _activityService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = activity.Id }, activity);
    }

    /// <summary>Updates an activity (Admin only). Protected against quota and overlap violations.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ActivityResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ActivityResponseDto>> Update([FromRoute] int id, [FromBody] UpdateActivityDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _activityService.UpdateAsync(id, dto, cancellationToken));
    }

    /// <summary>
    /// Soft delete (Admin only): cancels the activity (IsActive = false) and all its active registrations
    /// in one transaction. The row stays in the database and is still returned by GET with isActive = false.
    /// Repeating the call is harmless.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] int id, CancellationToken cancellationToken)
    {
        await _activityService.CancelActivityAsync(id, cancellationToken);
        return NoContent();
    }
}
