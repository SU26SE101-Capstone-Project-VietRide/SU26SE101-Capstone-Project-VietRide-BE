using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Abstractions.Services;
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

    [HttpGet("shuttle-requests")]
    [Authorize(Roles = "OPERATOR_STAFF,OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<ShuttleRequestPage>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShuttleRequestPage>> GetRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = GetOperatorId();
        return Ok(await _sender.Send(
            new GetShuttleRequestsQuery(operatorId, Math.Max(1, page), Math.Clamp(pageSize, 1, 100)),
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
            request.Notes), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    private Guid GetOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
}
