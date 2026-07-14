using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Application.Features.Trips.Operations;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/driver")]
[Authorize(Roles = "DRIVER,ASSISTANT")]
public sealed class DriverController : ControllerBase
{
    private readonly IMediator mediator;

    public DriverController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("me/schedule")]
    [ProducesResponseType(typeof(ApiResponse<GetMyDriverScheduleResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GetMyDriverScheduleResult>> GetMyScheduleAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        return Ok(await mediator.Send(
            new GetMyDriverScheduleQuery(userId, from, to),
            cancellationToken));
    }

    [HttpGet("trips/{tripId}/route")]
    [ProducesResponseType(typeof(ApiResponse<DriverTripRouteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DriverTripRouteDto>> GetAssignedTripRouteAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        return Ok(await mediator.Send(
            new GetAssignedTripRouteQuery(tripId, userId),
            cancellationToken));
    }

    [HttpPost("trips/{tripId:guid}/complete")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CompleteTripResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompleteTripResponse>> CompleteTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var actorUserId = CurrentUserClaims.GetUserId(User);
        return Ok(await mediator.Send(
            new CompleteTripCommand(tripId, actorUserId),
            cancellationToken));
    }
}
