using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/admin/booking-stats")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminBookingStatsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminBookingStatsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("aggregate")]
    [ProducesResponseType(typeof(ApiResponse<GetAdminBookingStatsAggregateResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GetAdminBookingStatsAggregateResult>> GetAggregate(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? groupBy,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminBookingStatsAggregateQuery(
                from,
                to,
                groupBy ?? "operator"),
            cancellationToken);

        return Ok(result);
    }
}
