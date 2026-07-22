using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Internal.Trips.BookRoundTripSeats;
using VietRide.Trip.Application.Features.Internal.Trips.BookSeats;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;
using VietRide.Trip.Application.Features.Internal.Trips.LockRoundTripSeats;
using VietRide.Trip.Application.Features.Internal.Trips.LockSeats;
using VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;
using VietRide.Trip.Application.Features.Internal.Trips.ReleaseSeats;
using VietRide.Trip.Application.Features.Internal.Trips.Requests;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/trips")]
public sealed class InternalTripsController : ControllerBase
{
    private static readonly string[] Rfc3339Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    private readonly IMediator mediator;

    public InternalTripsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("{tripId:guid}")]
    [ProducesResponseType(typeof(InternalTripSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<InternalTripSnapshotDto>> GetAsync(
        Guid tripId,
        [FromQuery] string? pricingAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? parsedPricingAt = null;
        if (pricingAt is not null)
        {
            if (!DateTimeOffset.TryParseExact(
                    pricingAt,
                    Rfc3339Formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var value))
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "pricingAt must be a valid RFC 3339 timestamp.",
                    [new ValidationError("pricingAt", "pricingAt must be a valid RFC 3339 timestamp.")]);
            }

            parsedPricingAt = value;
        }

        var result = await mediator.Send(new GetTripSnapshotQuery(tripId, parsedPricingAt), cancellationToken);
        return Ok(result);
    }

    [HttpGet("parcel-availability")]
    [ProducesResponseType(typeof(PagedResult<ParcelTripAvailabilityItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<ParcelTripAvailabilityItemDto>>> SearchParcelAvailabilityAsync(
        [FromQuery] Guid originStationId,
        [FromQuery] Guid destinationStationId,
        [FromQuery] DateOnly departureDate,
        [FromQuery] decimal estimatedWeightKg,
        [FromQuery] decimal estimatedVolumeM3,
        [FromQuery] string sizeCategory,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new SearchParcelAvailableTripsQuery(
                originStationId,
                destinationStationId,
                departureDate,
                estimatedWeightKg,
                estimatedVolumeM3,
                sizeCategory,
                page,
                pageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{tripId:guid}/tracking-authorization")]
    [ProducesResponseType(typeof(ApiResponse<TrackingAuthorizationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TrackingAuthorizationResponse>>> GetTrackingAuthorizationAsync(
        Guid tripId,
        [FromQuery] Guid? userId,
        [FromQuery] string? role,
        [FromQuery] Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetTripTrackingAuthorizationQuery(tripId, userId, role, operatorId),
            cancellationToken);

        return Ok(ApiResponse<TrackingAuthorizationResponse>.Ok(result, ApiMeta.Create(HttpContext.TraceIdentifier)));
    }

    [HttpGet("{tripId:guid}/route-stops")]
    [ProducesResponseType(typeof(ApiResponse<TripRouteStopsTrackingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TripRouteStopsTrackingResponse>>> GetRouteStopsForTrackingAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTripRouteStopsTrackingQuery(tripId), cancellationToken);
        return Ok(ApiResponse<TripRouteStopsTrackingResponse>.Ok(result, ApiMeta.Create(HttpContext.TraceIdentifier)));
    }

    [HttpGet("{tripId:guid}/route-geometry")]
    [ProducesResponseType(typeof(ApiResponse<TripRouteGeometryTrackingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TripRouteGeometryTrackingResponse>>> GetRouteGeometryForTrackingAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTripRouteGeometryTrackingQuery(tripId), cancellationToken);
        return Ok(ApiResponse<TripRouteGeometryTrackingResponse>.Ok(result, ApiMeta.Create(HttpContext.TraceIdentifier)));
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

    [HttpPost("round-trip/book-seats")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BookRoundTripSeatsAsync(
        [FromBody] BookRoundTripSeatsRequest request, CancellationToken cancellationToken)
    {
        static BookRoundTripSeatsLeg Map(BookRoundTripSeatsLegRequest leg)
            => new(leg.TripId, leg.SeatLockToken, leg.BookingId, leg.PassengerSeatAssignments);
        await mediator.Send(new BookRoundTripSeatsCommand(Map(request.Outbound), Map(request.Return)), cancellationToken);
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

    [HttpPost("{tripId:guid}/cargo/reserve")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CargoCapacityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoCapacityDto>> ReserveCargoAsync(
        Guid tripId,
        [FromBody] CargoMutationRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new CargoMutationCommand(tripId, request.ParcelId, request.WeightKg, request.VolumeM3, request.AllowCapacityOverflow, "reserve"),
            cancellationToken));
    }

    [HttpGet("{tripId:guid}/cargo/capacity")]
    [ProducesResponseType(typeof(ApiResponse<CargoCapacityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargoCapacityDto>> GetCargoCapacityAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetCargoCapacityQuery(tripId, OperatorId: null), cancellationToken));
    }

    [HttpPost("{tripId:guid}/cargo/remeasure")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CargoCapacityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoCapacityDto>> RemeasureCargoAsync(
        Guid tripId,
        [FromBody] CargoMutationRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new CargoMutationCommand(tripId, request.ParcelId, request.WeightKg, request.VolumeM3, request.AllowCapacityOverflow, "remeasure"),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/cargo/load")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CargoCapacityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoCapacityDto>> LoadCargoAsync(
        Guid tripId,
        [FromBody] CargoMutationRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new CargoMutationCommand(tripId, request.ParcelId, request.WeightKg, request.VolumeM3, request.AllowCapacityOverflow, "load"),
            cancellationToken));
    }

    [HttpPost("{tripId:guid}/cargo/release")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CargoCapacityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargoCapacityDto>> ReleaseCargoAsync(
        Guid tripId,
        [FromBody] CargoMutationRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new CargoMutationCommand(tripId, request.ParcelId, request.WeightKg, request.VolumeM3, request.AllowCapacityOverflow, "release"),
            cancellationToken));
    }

    [HttpPost("round-trip/lock-seats")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<LockRoundTripSeatsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<LockRoundTripSeatsResult>>> LockRoundTripSeatsAsync(
        [FromBody] LockRoundTripSeatsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Idempotency-Key header is required.",
                [new ValidationError("Idempotency-Key", "Idempotency-Key header is required.")]);
        }

        var result = await mediator.Send(
            new LockRoundTripSeatsCommand(
                new Application.Features.Internal.Trips.LockRoundTripSeats.LockRoundTripSeatsLegRequest(
                    request.Outbound.TripId,
                    request.Outbound.SeatNumbers),
                new Application.Features.Internal.Trips.LockRoundTripSeats.LockRoundTripSeatsLegRequest(
                    request.Return.TripId,
                    request.Return.SeatNumbers),
                request.HoldOwnerId,
                request.TtlSeconds,
                idempotencyKey.Trim()),
            cancellationToken);

        var traceId = HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var id)
            ? id?.ToString() ?? string.Empty
            : HttpContext.TraceIdentifier;

        return Ok(ApiResponse<LockRoundTripSeatsResult>.Ok(result, ApiMeta.Create(traceId)));
    }
}
