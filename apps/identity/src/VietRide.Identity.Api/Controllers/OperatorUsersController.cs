using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Auth.ResendInitialPassword;
using VietRide.Identity.Application.Features.OperatorUsers.CreateOperatorUser;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// Operator-scoped user-management endpoints.
/// Success and error responses are wrapped by ADR 0004 ApiResponse filters.
/// </summary>
[ApiController]
[Authorize]
[Route("v1/operator/users")]
public sealed class OperatorUsersController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorUsersController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Create a Driver, Assistant, or Operator Staff user under the caller's operator.</summary>
    /// <remarks>
    /// Caller must be OPERATOR_ADMIN and the current operator must be APPROVED.
    /// There is no Idempotency-Key requirement for this endpoint.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateOperatorUserResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateOperatorUserRequest request, CancellationToken ct)
    {
        var callerUserId = CurrentUserClaims.GetUserId(User);
        var callerRole = CurrentUserClaims.GetRole(User);
        var callerOperatorId = CurrentUserClaims.GetOperatorId(User);

        var result = await _sender.Send(
            new CreateOperatorUserCommand(
                request.Email,
                request.Phone,
                request.DisplayName,
                request.Role,
                callerUserId,
                callerRole,
                callerOperatorId),
            ct);

        return Created($"/v1/operator/users/{result.UserId}", result);
    }

    /// <summary>Resend the initial-password setup link for an operator-scoped user.</summary>
    /// <remarks>
    /// Caller must be OPERATOR_ADMIN and scoped to the same operator as the target user.
    /// There is no Idempotency-Key requirement for this endpoint.
    /// </remarks>
    [HttpPost("{userId:guid}/resend-initial-password")]
    [ProducesResponseType(typeof(ApiResponse<ResendInitialPasswordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResendInitialPassword(Guid userId, CancellationToken ct)
    {
        var callerUserId = CurrentUserClaims.GetUserId(User);
        var callerRole = CurrentUserClaims.GetRole(User);
        var callerOperatorId = CurrentUserClaims.GetOperatorId(User);

        var result = await _sender.Send(
            new ResendInitialPasswordCommand(
                userId,
                callerUserId,
                callerRole,
                callerOperatorId),
            ct);

        return Ok(result);
    }
}
