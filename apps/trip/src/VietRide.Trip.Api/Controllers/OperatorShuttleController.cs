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

    [HttpGet("shuttle-requests")]
    [AllowedQueryParameters("page", "pageSize", "from", "to", "mainTripId", "search")]
    [Authorize(Roles = "OPERATOR_STAFF,OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ShuttleRequestTripGroup>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ShuttleRequestTripGroup>>> GetRequests(
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
            GetOperatorId(), shuttleTripId, request.Reason), cancellationToken));

    private Guid GetOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
}
