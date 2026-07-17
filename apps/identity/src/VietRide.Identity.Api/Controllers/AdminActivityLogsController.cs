using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Admin.ListActivityLogs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Authorize(Roles = "SYSTEM_ADMIN")]
[Route("v1/admin/activity-logs")]
public sealed class AdminActivityLogsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminActivityLogsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminActivityLogItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<AdminActivityLogItemDto>>> ListActivityLogs(
        [FromQuery] Guid? userId,
        [FromQuery] string? action,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListActivityLogsQuery(
                CurrentUserClaims.GetRole(User),
                userId,
                action,
                from,
                to,
                page,
                pageSize),
            cancellationToken);

        return Ok(result);
    }
}
