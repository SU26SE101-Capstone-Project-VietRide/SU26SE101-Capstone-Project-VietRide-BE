using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;
using VietRide.Trip.Application.Features.DriverTrips.CompleteTrip;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Application.Features.DriverTrips.StartTrip;
using VietRide.Trip.Application.Features.Incidents.ReportIncident;
using ArriveTripDestinationCommand = VietRide.Trip.Application.Features.Trips.Operations.ArriveTripDestinationCommand;
using ArriveTripDestinationResponse = VietRide.Trip.Application.Features.Trips.Operations.ArriveTripDestinationResponse;
using ArriveTripStopCommand = VietRide.Trip.Application.Features.Trips.Operations.ArriveTripStopCommand;
using ArriveTripStopResponse = VietRide.Trip.Application.Features.Trips.Operations.ArriveTripStopResponse;

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

    [HttpPost("trips/{tripId}/start")]
    [Authorize(Roles = "DRIVER")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<StartTripResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StartTripResponse>> StartTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        return Ok(await mediator.Send(new StartTripCommand(tripId, userId), cancellationToken));
    }

    [HttpPost("trips/{tripId}/complete")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<CompleteTripResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CompleteTripResponse>> CompleteTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var role = User.IsInRole("DRIVER") ? "DRIVER" : "ASSISTANT";
        return Ok(await mediator.Send(
            new CompleteTripCommand(tripId, userId, role),
            cancellationToken));
    }

    [HttpPost("trips/{tripId}/incident")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<ReportIncidentResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReportIncidentResponse>> ReportIncidentAsync(
        Guid tripId,
        [FromBody] ReportIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var reporterUserId = CurrentUserClaims.GetUserId(User);
        var response = await mediator.Send(
            new ReportIncidentCommand(
                tripId,
                reporterUserId,
                request.Category,
                request.Description,
                request.PhotoUrls,
                request.Latitude,
                request.Longitude),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("trips/{tripId:guid}/stops/{stopId:guid}/arrive")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<ArriveTripStopResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ArriveTripStopResponse>> ArriveStopAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        var actorUserId = CurrentUserClaims.GetUserId(User);
        return Ok(await mediator.Send(
            new ArriveTripStopCommand(tripId, stopId, actorUserId),
            cancellationToken));
    }

    [HttpPost("trips/{tripId:guid}/destination/arrive")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<ArriveTripDestinationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ArriveTripDestinationResponse>> ArriveDestinationAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var actorUserId = CurrentUserClaims.GetUserId(User);
        return Ok(await mediator.Send(
            new ArriveTripDestinationCommand(tripId, actorUserId),
            cancellationToken));
    }
}
