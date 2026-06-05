using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Users.CompleteProfile;
using VietRide.Identity.Application.Features.Users.GetMe;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// User profile endpoints for the authenticated caller.
/// Success and error responses are wrapped by ADR 0004 ApiResponse filters.
/// </summary>
[ApiController]
[Authorize]
[Route("v1/users")]
public sealed class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Return the authenticated caller profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<GetMeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var result = await _sender.Send(new GetMeQuery(CurrentUserClaims.GetUserId(User)), ct);
        return Ok(result);
    }

    /// <summary>Complete the authenticated caller profile by setting an E.164 VN phone once.</summary>
    /// <remarks>
    /// Invalid phone format → 400 AUTH_PHONE_INVALID_FORMAT.
    /// Duplicate phone → 409 AUTH_PHONE_ALREADY_REGISTERED.
    /// Existing phone → 422 VALIDATION_ERROR.
    /// </remarks>
    [HttpPost("me/complete-profile")]
    [ProducesResponseType(typeof(ApiResponse<CompleteProfileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CompleteProfile(
        [FromBody] CompleteProfileRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new CompleteProfileCommand(CurrentUserClaims.GetUserId(User), request.Phone),
            ct);

        return Ok(result);
    }
}
