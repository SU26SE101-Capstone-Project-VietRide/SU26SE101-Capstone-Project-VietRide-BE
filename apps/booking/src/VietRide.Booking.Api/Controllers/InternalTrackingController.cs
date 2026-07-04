using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Internal.Tracking;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("internal/v1/trips")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalTrackingController : ControllerBase
{
    private readonly IMediator mediator;

    public InternalTrackingController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("{tripId:guid}/tracking-authorization/bookings")]
    [ProducesResponseType(typeof(ApiResponse<TrackingBookingAuthorizationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<TrackingBookingAuthorizationResponse>>> GetTrackingAuthorizationAsync(
        Guid tripId,
        [FromQuery] Guid? userId,
        [FromQuery] string? role,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetBookingTrackingAuthorizationQuery(tripId, userId, role),
            cancellationToken);
        return Ok(ApiResponse<TrackingBookingAuthorizationResponse>.Ok(result, ApiMeta.Create(HttpContext.TraceIdentifier)));
    }

    [HttpGet("{tripId:guid}/stops/{stopId:guid}/pickup-bookings")]
    [ProducesResponseType(typeof(ApiResponse<PickupBookingsTrackingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PickupBookingsTrackingResponse>>> GetPickupBookingsAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetPickupBookingsTrackingQuery(tripId, stopId),
            cancellationToken);
        return Ok(ApiResponse<PickupBookingsTrackingResponse>.Ok(result, ApiMeta.Create(HttpContext.TraceIdentifier)));
    }
}
