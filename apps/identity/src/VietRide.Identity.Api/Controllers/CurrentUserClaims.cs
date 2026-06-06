using System.Security.Claims;

namespace VietRide.Identity.Api.Controllers;

internal static class CurrentUserClaims
{
    public static Guid GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");

        return userId;
    }

    public static string GetRole(ClaimsPrincipal user)
        => user.FindFirstValue("role")
            ?? user.FindFirstValue(ClaimTypes.Role)
            ?? string.Empty;

    public static Guid? GetOperatorId(ClaimsPrincipal user)
    {
        var operatorId = user.FindFirstValue("operatorId");
        return Guid.TryParse(operatorId, out var parsed) ? parsed : null;
    }
}
