using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Application.Features.Trips.EditTrip;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Trips.Operations;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/trips")]
public sealed class OperatorTripsController : ControllerBase
{
    private const string OperatorReadRoles = "OPERATOR_STAFF,OPERATOR_ADMIN";
    private const string OperatorWriteRoles = "OPERATOR_ADMIN";

    private readonly IMediator mediator;

    public OperatorTripsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpPatch("{tripId:guid}")]
    [RequireIdempotency]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<TripDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TripDetailDto>> EditAsync(
        Guid tripId,
        [FromBody] EditTripRequest request,
        CancellationToken cancellationToken)
    {
        var requestId = HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var value)
            && value is string traceId
            && !string.IsNullOrWhiteSpace(traceId)
                ? traceId
                : HttpContext.TraceIdentifier;

        return Ok(await mediator.Send(
            new EditTripCommand(
                tripId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                requestId,
                request.BaseFareSpecified,
                request.BaseFare,
                request.NotesSpecified,
                request.Notes,
                request.VehicleIdSpecified,
                request.VehicleId,
                request.RouteIdSpecified,
                request.RouteId),
            cancellationToken));
    }

    [HttpGet("{tripId:guid}/cargo-capacity")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<CargoCapacityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargoCapacityDto>> GetCargoCapacityAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new GetCargoCapacityQuery(tripId, GetRequiredOperatorId()),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/stops/{stopId:guid}/arrive")]
    [RequireIdempotencyKey]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<ArriveTripStopResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArriveTripStopResponse>> ArriveStopAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ArriveTripStopCommand(
                tripId,
                stopId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User)),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/substitute-vehicle")]
    [RequireIdempotencyKey]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<SubstituteVehicleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubstituteVehicleResponse>> SubstituteVehicleAsync(
        Guid tripId,
        [FromBody] SubstituteVehicleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new SubstituteVehicleCommand(
                tripId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                request.NewVehicleId,
                request.NewDriverUserId,
                request.NewAssistantUserId,
                request.Reason),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/disrupt-no-substitution")]
    [RequireIdempotencyKey]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<DisruptNoSubstitutionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisruptNoSubstitutionResponse>> DisruptNoSubstitutionAsync(
        Guid tripId,
        [FromBody] DisruptNoSubstitutionRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new DisruptNoSubstitutionCommand(
                tripId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                request.Reason),
            cancellationToken));
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage trips.");
}
