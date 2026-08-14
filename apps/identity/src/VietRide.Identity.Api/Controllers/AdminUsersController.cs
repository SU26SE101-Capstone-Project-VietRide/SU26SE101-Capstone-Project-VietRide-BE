using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Admin.CreateAdminUser;
using VietRide.Identity.Application.Features.Admin.ListUsers;
using VietRide.Identity.Application.Features.Admin.LockUser;
using VietRide.Identity.Application.Features.Admin.UnlockUser;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// Administrative user-management endpoints.
/// Success and error responses are wrapped by ADR 0004 ApiResponse filters.
/// </summary>
[ApiController]
[Authorize(Roles = "SYSTEM_ADMIN")]
[Route("v1/admin/users")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly ISender _sender;

    public AdminUsersController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Lists users across the platform without exposing authentication secrets.</summary>
    [HttpGet]
    [AllowedQueryParameters("search", "role", "status", "operatorId", "includeDeleted", "page", "pageSize", "sortBy", "sortDir", "from", "to")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminUserListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<AdminUserListItemDto>>> ListUsers(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] Guid? operatorId,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new ListUsersQuery(
                CurrentUserClaims.GetRole(User),
                search,
                role,
                status,
                operatorId,
                includeDeleted,
                page,
                pageSize,
                sortBy,
                sortDir,
                from,
                to),
            ct);

        return Ok(result);
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

    /// <summary>Locks a User and revokes every active refresh token.</summary>
    [HttpPost("{userId:guid}/lock")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<LockUserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LockUserResponseDto>> LockUser(
        Guid userId,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new LockUserCommand(
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetRole(User),
                userId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString()),
            ct);

        return Ok(result);
    }

    /// <summary>Unlocks a User to its recorded origin and resets login lockout state.</summary>
    [HttpPost("{userId:guid}/unlock")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<UnlockUserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UnlockUserResponseDto>> UnlockUser(
        Guid userId,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new UnlockUserCommand(
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetRole(User),
                userId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString()),
            ct);

        return Ok(result);
    }
}
