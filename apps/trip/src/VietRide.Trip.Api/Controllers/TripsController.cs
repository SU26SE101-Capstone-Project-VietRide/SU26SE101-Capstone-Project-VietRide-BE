using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
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
        [FromQuery] Guid originStationId,
        [FromQuery] Guid destinationStationId,
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
                allowAlongRoutePickup),
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
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<TripSeatMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripSeatMapDto>> GetSeatMapAsync(Guid tripId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetTripSeatMapQuery(tripId), cancellationToken));
    }
}
