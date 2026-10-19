using LevBakehila.API.Extensions;
using LevBakehila.Core.DTOs.Common;
using LevBakehila.Core.DTOs.Users;
using LevBakehila.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LevBakehila.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>Lists all users with paging and sorting (Admin only). SortBy: fullName | email | neighborhood.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(PagedResult<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserResponseDto>>> GetAll([FromQuery] PaginationParams pagination, CancellationToken cancellationToken)
    {
        return Ok(await _userService.GetAllAsync(pagination, cancellationToken));
    }

    /// <summary>Gets one user by id (Admin only).</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(UserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponseDto>> GetById([FromRoute] int id, CancellationToken cancellationToken)
    {
        return Ok(await _userService.GetByIdAsync(id, cancellationToken));
    }

    /// <summary>Gets the profile of the logged-in user.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponseDto>> GetMe(CancellationToken cancellationToken)
    {
        return Ok(await _userService.GetProfileAsync(User.GetUserId(), cancellationToken));
    }

    /// <summary>Updates name, neighborhood and skills of the logged-in user.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponseDto>> UpdateMe([FromBody] UpdateProfileDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _userService.UpdateProfileAsync(User.GetUserId(), dto, cancellationToken));
    }

    /// <summary>Deletes a user without active registrations (Admin only).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        await _userService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
