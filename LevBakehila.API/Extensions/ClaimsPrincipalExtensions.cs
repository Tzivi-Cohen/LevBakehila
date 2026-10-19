using System.Security.Claims;
using LevBakehila.Core.Enums;
using LevBakehila.Core.Exceptions;

namespace LevBakehila.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    // The user id always comes from the validated JWT - never from the request body
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out int id)
            ? id
            : throw new UnauthorizedException("הטוקן אינו מכיל מזהה משתמשת.");
    }

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Admin));
}
