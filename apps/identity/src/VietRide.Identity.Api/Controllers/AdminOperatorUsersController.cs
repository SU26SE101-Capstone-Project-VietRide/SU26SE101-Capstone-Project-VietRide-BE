using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Application.Features.OperatorUsers.ListOperatorUsers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// System-admin views over operator-scoped employees.
/// Success and error responses are wrapped by ADR 0004 ApiResponse filters.
/// </summary>
[ApiController]
[Authorize]
[Route("v1/admin/operator-users")]
public sealed class AdminOperatorUsersController : ControllerBase
{
    private readonly ISender _sender;

    public AdminOperatorUsersController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Lists Driver, Assistant, and Operator Staff users across operators.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorUserListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<OperatorUserListItemDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] Guid? operatorId,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new ListOperatorUsersQuery(
                ListOperatorUsersScope.Admin,
                CurrentUserClaims.GetRole(User),
                CurrentUserClaims.GetOperatorId(User),
                operatorId,
                page,
                pageSize,
                search,
                sortBy,
                sortDir,
                role,
                status),
            ct);

        return Ok(result);
    }
}
