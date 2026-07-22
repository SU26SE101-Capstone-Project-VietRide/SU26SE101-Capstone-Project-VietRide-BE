using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Application.Features.Admin.OutboxDlq;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Authorize(Roles = "SYSTEM_ADMIN")]
[Route("v1/admin/outbox/dlq")]
public sealed class AdminOutboxDlqController : ControllerBase
{
    private readonly ISender _sender;

    public AdminOutboxDlqController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminOutboxDlqResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminOutboxDlqResponseDto>> GetAsync(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? service = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string sortDir = "desc",
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new GetAdminOutboxDlqQuery(cursor, pageSize, service, eventType, sortDir),
            cancellationToken).ConfigureAwait(false);

        return Ok(result);
    }
}
