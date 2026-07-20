using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/outbox/dlq")]
public sealed class InternalOutboxDlqController : ControllerBase
{
    private readonly IOutboxDlqReader _reader;

    public InternalOutboxDlqController(IOutboxDlqReader reader) => _reader = reader;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OutboxDlqReadItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<OutboxDlqReadItem>>> GetAsync([FromQuery] string? eventType, [FromQuery] int pageSize = 100, [FromQuery] DateTimeOffset? afterTerminalAt = null, [FromQuery] Guid? afterId = null, [FromQuery] string sortDir = "desc", CancellationToken cancellationToken = default)
        => Ok(await _reader.ReadAsync(eventType, pageSize, afterTerminalAt, afterId, !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase), cancellationToken));
}
