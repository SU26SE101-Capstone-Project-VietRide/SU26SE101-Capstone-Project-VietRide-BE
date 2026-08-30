using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Application.Features.Trips.EditTrip;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;
using VietRide.Trip.Application.Features.Trips.ListOperatorTrips;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Application.Features.Trips.SeatOperations;
using VietRide.Trip.Application.Features.Trips.StartTripBoarding;

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

    [HttpGet]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorTripListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<OperatorTripListItemDto>>> ListAsync(
        [FromQuery] string? search,
        [FromQuery] OperatorTripStatusFilter? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ListOperatorTripsQuery(
                GetRequiredOperatorId(),
                search,
                status,
                from,
                to,
                page,
                pageSize,
                sortBy,
                sortDir),
            cancellationToken));
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

    [HttpPost("{tripId}/boarding")]
    [Authorize(Roles = OperatorWriteRoles)]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<StartTripBoardingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StartTripBoardingResponse>> StartBoardingAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new StartTripBoardingCommand(
                tripId,
                CurrentUserClaims.GetUserId(User),
                "OPERATOR_ADMIN",
                GetRequiredOperatorId()),
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

    [HttpPost("{tripId:guid}/substitute-vehicle")]
    [RequireIdempotency]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<SubstituteVehicleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
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
                request.ReplacementVehicleId,
                request.EstimatedRecoveryDepartureAt,
                request.Reason,
                request.IncidentId,
                request.NotifyPassengers,
                request.ReplacementCrew?.DriverId,
                request.ReplacementCrew?.AssistantId,
                request.ReplacementCrew is not null,
                request.AcknowledgeInsufficientSeats,
                request.PreviewToken,
                request.SeatAssignments?
                    .Select(assignment => new SubstituteVehicleSeatAssignment(
                        assignment.PassengerId,
                        assignment.NewSeatNumber))
                    .ToArray()),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/substitute-vehicle/preview")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<SubstituteVehiclePreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubstituteVehiclePreviewResponse>> PreviewSubstituteVehicleAsync(
        Guid tripId,
        [FromBody] PreviewSubstituteVehicleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new PreviewSubstituteVehicleQuery(
                tripId,
                GetRequiredOperatorId(),
                request.ReplacementVehicleId),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/disrupt-no-substitution")]
    [RequireIdempotency]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<DisruptNoSubstitutionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
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

    [HttpPost("{tripId:guid}/seats/{seatNumber}/disable")]
    [RequireIdempotency]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<TripSeatMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TripSeatMapDto>> DisableSeatAsync(
        Guid tripId,
        string seatNumber,
        [FromBody] DisableTripSeatRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new DisableTripSeatCommand(
                tripId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                seatNumber,
                request.Reason,
                GetRequestId()),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/seats/{seatNumber}/enable")]
    [RequireIdempotency]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<TripSeatMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TripSeatMapDto>> EnableSeatAsync(
        Guid tripId,
        string seatNumber,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new EnableTripSeatCommand(
                tripId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                seatNumber,
                GetRequestId()),
            cancellationToken));
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage trips.");

    private string GetRequestId()
        => HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var value)
            && value is string requestId
            && !string.IsNullOrWhiteSpace(requestId)
                ? requestId
                : HttpContext.TraceIdentifier;
}
