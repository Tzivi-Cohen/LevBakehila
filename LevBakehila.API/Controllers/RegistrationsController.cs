using LevBakehila.API.Extensions;
using LevBakehila.Core.DTOs.Registrations;
using LevBakehila.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LevBakehila.API.Controllers;

[ApiController]
[Route("api/registrations")]
[Authorize]
[Produces("application/json")]
public class RegistrationsController : ControllerBase
{
    private readonly IRegistrationService _registrationService;

    public RegistrationsController(IRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    /// <summary>Registers the logged-in volunteer to an activity. Returns 409 on a full activity, overlap or a parallel conflict.</summary>
    [HttpPost]
    [Authorize(Roles = "Volunteer")]
    [ProducesResponseType(typeof(RegistrationResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistrationResponseDto>> Register([FromBody] CreateRegistrationDto dto, CancellationToken cancellationToken)
    {
        var registration = await _registrationService.RegisterAsync(User.GetUserId(), dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = registration.Id }, registration);
    }

    /// <summary>Gets one registration. Allowed for the volunteer herself or the Admin.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RegistrationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationResponseDto>> GetById([FromRoute] int id, CancellationToken cancellationToken)
    {
        return Ok(await _registrationService.GetByIdAsync(id, User.GetUserId(), User.IsAdmin(), cancellationToken));
    }

    /// <summary>Lists the registrations of the logged-in volunteer.</summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(List<RegistrationResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RegistrationResponseDto>>> GetMine(CancellationToken cancellationToken)
    {
        return Ok(await _registrationService.GetMyRegistrationsAsync(User.GetUserId(), cancellationToken));
    }

    /// <summary>Cancels a registration. Allowed for the volunteer herself or the Admin.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] int id, CancellationToken cancellationToken)
    {
        await _registrationService.CancelAsync(id, User.GetUserId(), User.IsAdmin(), cancellationToken);
        return NoContent();
    }
}
