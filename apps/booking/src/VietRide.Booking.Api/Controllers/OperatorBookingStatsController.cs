using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/operator/booking-stats")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorBookingStatsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OperatorBookingStatsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<GetOperatorBookingStatsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GetOperatorBookingStatsResult>> Get(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? groupBy,
        CancellationToken cancellationToken)
    {
        if (!TryGetOperatorId(User, out var operatorId))
        {
            return Forbid();
        }

        var result = await _mediator.Send(
            new GetOperatorBookingStatsQuery(
                operatorId,
                from,
                to,
                groupBy ?? "date"),
            cancellationToken);

        return Ok(result);
    }

    private static bool TryGetOperatorId(ClaimsPrincipal user, out Guid operatorId)
    {
        var value = user.FindFirstValue("operator_id")
            ?? user.FindFirstValue("operatorId");

        return Guid.TryParse(value, out operatorId);
    }
}
