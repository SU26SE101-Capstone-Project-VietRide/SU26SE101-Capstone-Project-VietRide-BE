using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Devices.RegisterDeviceToken;
using VietRide.Identity.Application.Features.Devices.RemoveDeviceToken;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// Device-token endpoints for the authenticated caller.
/// Success and error responses are wrapped by ADR 0004 ApiResponse filters, except 204 No Content.
/// </summary>
[ApiController]
[Authorize]
[Route("v1/auth")]
public sealed class DevicesController : ControllerBase
{
    private readonly ISender _sender;

    public DevicesController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Register or refresh the authenticated caller FCM device token.</summary>
    /// <remarks>
    /// Upserts by (caller user id, fcmToken), reactivates logged-out rows, and claims an active token from another user.
    /// </remarks>
    [HttpPost("device-token")]
    [ProducesResponseType(typeof(ApiResponse<RegisterDeviceTokenResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegisterDeviceToken(
        [FromBody] RegisterDeviceTokenRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new RegisterDeviceTokenCommand(
                CurrentUserClaims.GetUserId(User),
                request.FcmToken,
                request.Platform),
            ct);

        return Ok(result);
    }

    /// <summary>Deactivate the authenticated caller FCM device token.</summary>
    /// <remarks>204 No Content — empty body (not wrapped in envelope per ADR 0004 Rule 2).</remarks>
    [HttpDelete("device-token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveDeviceToken(
        [FromBody] RemoveDeviceTokenRequest? request,
        CancellationToken ct)
    {
        await _sender.Send(new RemoveDeviceTokenCommand(CurrentUserClaims.GetUserId(User), request?.FcmToken), ct);
        return NoContent();
    }
}
