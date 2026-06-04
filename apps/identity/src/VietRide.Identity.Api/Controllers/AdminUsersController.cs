using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Admin.CreateAdminUser;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// Administrative user-management endpoints.
/// Success and error responses are wrapped by ADR 0004 ApiResponse filters.
/// </summary>
[ApiController]
[Authorize]
[Route("v1/admin/users")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly ISender _sender;

    public AdminUsersController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Create a passwordless SYSTEM_ADMIN user pending initial password setup.</summary>
    /// <remarks>
    /// Caller must be SYSTEM_ADMIN. Initial-password token creation and email send are deferred to Day 5.
    /// Non-SYSTEM_ADMIN caller → 403 FORBIDDEN.
    /// Duplicate email → 409 AUTH_EMAIL_ALREADY_REGISTERED.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateAdminUserResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAdminUser(
        [FromBody] CreateAdminUserRequest request,
        CancellationToken ct)
    {
        var callerUserId = CurrentUserClaims.GetUserId(User);
        var callerRole = CurrentUserClaims.GetRole(User);

        if (!string.Equals(callerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can create admin users.");

        var result = await _sender.Send(
            new CreateAdminUserCommand(
                callerUserId,
                callerRole,
                request.Email,
                request.DisplayName,
                request.Role),
            ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }
}
