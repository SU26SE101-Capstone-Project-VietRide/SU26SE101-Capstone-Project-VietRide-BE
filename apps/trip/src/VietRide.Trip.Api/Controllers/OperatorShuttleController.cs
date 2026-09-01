using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.ResourceAvailability;
using VietRide.Trip.Application.Features.Shuttle;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator")]
public sealed class OperatorShuttleController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorShuttleController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("shuttle-trips")]
    [Authorize(Roles = "OPERATOR_STAFF,OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorShuttleTripListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PagedResult<OperatorShuttleTripListItemDto>>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var statuses = string.IsNullOrWhiteSpace(status)
            ? null
            : status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.ToUpperInvariant())
                .ToArray();
        return Ok(await _sender.Send(
            new GetOperatorShuttleTripsQuery(
                GetOperatorId(),
                Math.Max(1, page),
                Math.Clamp(pageSize, 1, 100),
                from,
                to,
                statuses),
            cancellationToken));
    }

    [HttpGet("shuttle-trips/{shuttleTripId:guid}/passengers")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [ProducesResponseType(typeof(ApiResponse<ShuttlePassengerContactResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ShuttlePassengerContactResponse>> GetPassengerContacts(
        Guid shuttleTripId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Ok(await _sender.Send(
            new GetShuttlePassengerContactsQuery(GetOperatorId(), shuttleTripId),
            cancellationToken));
    }

    [HttpGet("shuttle-trips/{shuttleTripId:guid}/assignment-history")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ShuttleAssignmentHistoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ShuttleAssignmentHistoryItemDto>>> GetAssignmentHistory(
        Guid shuttleTripId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _sender.Send(
            new GetShuttleAssignmentHistoryQuery(
                GetOperatorId(),
                shuttleTripId,
                Math.Max(1, page),
                Math.Clamp(pageSize, 1, 100)),
            cancellationToken));

    [HttpGet("shuttle-requests")]
    [AllowedQueryParameters("page", "pageSize", "from", "to", "mainTripId", "search")]
    [Authorize(Roles = "OPERATOR_STAFF,OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<ShuttleRequestPage>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShuttleRequestPage>> GetRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? mainTripId = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var operatorId = GetOperatorId();
        return Ok(await _sender.Send(
            new GetShuttleRequestsQuery(
                operatorId, Math.Max(1, page), Math.Clamp(pageSize, 1, 100),
                from, to, mainTripId, search),
            cancellationToken));
    }

    [HttpPost("shuttle-trips")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CreateShuttleTripResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateShuttleTripResult>> CreateTrip(
        [FromBody] CreateShuttleTripRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new CreateShuttleTripCommand(
            GetOperatorId(),
            CurrentUserClaims.GetUserId(User),
            request.MainTripId,
            request.DriverUserId,
            request.VehicleId,
            request.ScheduledDepartureTime,
            request.ScheduledEndTime,
            request.OrderedBookingIds,
            request.Notes,
            request.Direction ?? string.Empty), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("shuttle-trips/{shuttleTripId:guid}/assignment")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReassignShuttleTripResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReassignShuttleTripResult>> ReassignTrip(
        Guid shuttleTripId,
        [FromBody] ReassignShuttleTripRequest request,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(
            new ReassignShuttleTripCommand(
                GetOperatorId(),
                CurrentUserClaims.GetUserId(User),
                shuttleTripId,
                request.DriverUserId,
                request.VehicleId,
                request.Reason),
            cancellationToken));

    [HttpPost("shuttle-trips/{shuttleTripId:guid}/bookings/{bookingId:guid}/unassign")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<UnassignShuttleBookingResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<UnassignShuttleBookingResult>> UnassignBooking(
        Guid shuttleTripId,
        Guid bookingId,
        [FromBody] CancelShuttleRequest request,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(
            new UnassignShuttleBookingCommand(
                GetOperatorId(),
                shuttleTripId,
                bookingId,
                CurrentUserClaims.GetUserId(User),
                request.Reason),
            cancellationToken));

    [HttpPost("shuttle-trips/availability-check")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [SkipIdempotency("Availability preview is read-only and never creates a reservation.")]
    [ProducesResponseType(typeof(ApiResponse<ResourceAvailabilityResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ResourceAvailabilityResult>> CheckAvailability(
        [FromBody] CheckShuttleAvailabilityRequest request,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(
            new CheckShuttleAvailabilityQuery(
                GetOperatorId(),
                request.MainTripId,
                request.Direction,
                request.DriverUserId,
                request.VehicleId,
                request.ScheduledDepartureTime,
                request.ScheduledEndTime,
                request.OrderedBookingIds),
            cancellationToken));

    [HttpPost("shuttle-trips/route-preview")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [SkipIdempotency("Shuttle route preview is read-only and never creates a reservation.")]
    [ProducesResponseType(typeof(ApiResponse<ShuttleRoutePreviewResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ShuttleRoutePreviewResult>> PreviewRoute(
        [FromBody] PreviewShuttleRouteRequest request,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(
            new PreviewShuttleRouteQuery(
                GetOperatorId(),
                request.MainTripId,
                request.Direction,
                request.ScheduledDepartureTime,
                request.OrderedBookingIds),
            cancellationToken));

    [HttpPost("shuttle-requests/{mainTripId:guid}/{bookingId:guid}/cancel")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [RequireIdempotency]
    public async Task<ActionResult<ShuttleLifecycleResult>> CancelRequest(
        Guid mainTripId,
        Guid bookingId,
        [FromQuery] string direction,
        [FromBody] CancelShuttleRequest request,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(new CancelShuttleRequestCommand(
            GetOperatorId(), mainTripId, bookingId, direction, request.Reason), cancellationToken));

    [HttpPost("shuttle-trips/{shuttleTripId:guid}/cancel")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [RequireIdempotency]
    public async Task<ActionResult<ShuttleLifecycleResult>> CancelTrip(
        Guid shuttleTripId,
        [FromBody] CancelShuttleRequest request,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(new CancelShuttleTripCommand(
            GetOperatorId(), shuttleTripId, CurrentUserClaims.GetUserId(User), request.Reason), cancellationToken));

    private Guid GetOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
}
