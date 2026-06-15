using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Internal.Trips.BookSeats;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;
using VietRide.Trip.Application.Features.Internal.Trips.LockSeats;
using VietRide.Trip.Application.Features.Internal.Trips.ReleaseSeats;
using VietRide.Trip.Application.Features.Internal.Trips.Requests;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/trips")]
public sealed class InternalTripsController : ControllerBase
{
    private readonly IMediator mediator;

    public InternalTripsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("{tripId:guid}")]
    [ProducesResponseType(typeof(InternalTripSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InternalTripSnapshotDto>> GetAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTripSnapshotQuery(tripId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{tripId:guid}/lock-seats")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<LockSeatsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<LockSeatsResult>>> LockSeatsAsync(
        Guid tripId,
        [FromBody] LockSeatsRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString();
        var result = await mediator.Send(
            new LockSeatsCommand(tripId, request.SeatNumbers, request.HoldOwnerId, request.TtlSeconds, idempotencyKey),
            cancellationToken);

        return Ok(ApiResponse<LockSeatsResult>.Ok(result, ApiMeta.Create(HttpContext.TraceIdentifier)));
    }

    [HttpPost("{tripId:guid}/release-seats")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReleaseSeatsAsync(
        Guid tripId,
        [FromBody] ReleaseSeatsRequest request,
        CancellationToken cancellationToken)
    {
        await mediator.Send(new ReleaseSeatsCommand(tripId, request.SeatLockToken, request.SeatNumbers), cancellationToken);
        return NoContent();
    }

    [HttpPost("{tripId:guid}/book-seats")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BookSeatsAsync(
        Guid tripId,
        [FromBody] BookSeatsRequest request,
        CancellationToken cancellationToken)
    {
        await mediator.Send(
            new BookSeatsCommand(tripId, request.SeatLockToken, request.BookingId, request.PassengerSeatAssignments),
            cancellationToken);
        return NoContent();
    }
}
