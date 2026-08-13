using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;
using VietRide.Trip.Application.Features.Trips.SearchTrips;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/trips")]
public sealed class TripsController : ControllerBase
{
    private readonly IMediator mediator;

    public TripsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<SearchTripsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SearchTripsResult>> SearchAsync(
        [FromQuery] Guid? originStationId,
        [FromQuery] Guid? destinationStationId,
        [FromQuery] string? originProvinceCode,
        [FromQuery] string? originWardCode,
        [FromQuery] string? originLocationCode,
        [FromQuery] string? destinationProvinceCode,
        [FromQuery] string? destinationWardCode,
        [FromQuery] string? destinationLocationCode,
        [FromQuery] DateOnly departureDate,
        [FromQuery] int passengerCount,
        [FromQuery] bool? allowAlongRoutePickup,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new SearchTripsQuery(
                originStationId,
                destinationStationId,
                departureDate,
                passengerCount,
                allowAlongRoutePickup,
                originProvinceCode,
                originWardCode,
                destinationProvinceCode,
                destinationWardCode,
                originLocationCode,
                destinationLocationCode),
            cancellationToken));
    }

    [HttpGet("{tripId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<TripDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripDetailDto>> GetAsync(Guid tripId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetTripDetailQuery(tripId), cancellationToken));
    }

    [HttpGet("{tripId:guid}/seat-map")]
    [AllowedQueryParameters]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<TripSeatMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripSeatMapDto>> GetSeatMapAsync(Guid tripId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetTripSeatMapQuery(tripId), cancellationToken));
    }

    [HttpPost("/v1/operator/trips/{tripId:guid}/cancel/preview")]
    [SkipIdempotency("This POST is a read-only cancellation impact preview.")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<CancelTripPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CancelTripPreviewResponse>> CancelPreviewAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new CancelTripPreviewQuery(tripId, GetRequiredOperatorId()),
            cancellationToken));
    }

    [HttpPost("/v1/operator/trips/{tripId:guid}/cancel")]
    [RequireIdempotencyKey]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<CancelTripResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CancelTripResponse>> CancelAsync(
        Guid tripId,
        [FromBody] CancelTripRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new CancelTripCommand(
                tripId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                request.Reason),
            cancellationToken));
    }

    [HttpPost("/v1/operator/trips/{tripId:guid}/change-route")]
    [RequireIdempotencyKey]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<ChangeTripRouteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChangeTripRouteResponse>> ChangeRouteAsync(
        Guid tripId,
        [FromBody] ChangeTripRouteRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ChangeTripRouteCommand(
                tripId,
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                request.AlternativeRouteId),
            cancellationToken));
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage trips.");

}
