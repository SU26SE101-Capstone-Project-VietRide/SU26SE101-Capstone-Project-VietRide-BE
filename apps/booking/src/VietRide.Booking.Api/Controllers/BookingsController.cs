using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Bookings.CancelBooking;
using VietRide.Booking.Application.Features.Bookings.CreateBooking;
using VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;
using VietRide.Booking.Application.Features.Bookings.EditDropoff;
using VietRide.Booking.Application.Features.Bookings.EditPickup;
using VietRide.Booking.Application.Features.Bookings.GetBookingStatus;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

/// <summary>
/// Booking endpoints — POST /v1/bookings and future history/cancel/edit (Day 13+).
/// All success responses wrapped in <see cref="ApiResponse{T}"/> by ApiResponseResultFilter (ADR 0004).
/// All error responses wrapped by ApiResponseExceptionFilter.
/// </summary>
[ApiController]
[Route("v1/bookings")]
[Authorize]
public sealed class BookingsController : ControllerBase
{
    private const string PassengerRole = "PASSENGER";
    private const string OperatorRoles = "OPERATOR_ADMIN,OPERATOR_STAFF";
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISender _sender;

    public BookingsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Poll the minimal Booking-owned state after a payment callback.</summary>
    /// <remarks>
    /// Auth: booking owner or an operator authorized for the booking's tenant. This projection
    /// deliberately excludes Payment-owned data. The detailed operator read remains at
    /// GET /v1/operator/bookings/{id}.
    /// </remarks>
    [HttpGet("{bookingId:guid}")]
    [Authorize(Roles = PassengerRole + "," + OperatorRoles)]
    [ProducesResponseType(typeof(ApiResponse<GetBookingStatusResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetBookingStatusResult>> GetBookingStatus(
        [FromRoute] Guid bookingId,
        CancellationToken ct)
    {
        var passengerUserId = User.IsInRole(PassengerRole) ? GetPassengerUserId() : (Guid?)null;
        var operatorId = IsOperator(User) && TryGetOperatorId(User, out var value) ? value : (Guid?)null;
        if (passengerUserId is null && operatorId is null)
        {
            return Forbid();
        }

        var result = await _sender.Send(new GetBookingStatusQuery(bookingId, passengerUserId, operatorId), ct);

        return Ok(result);
    }

    /// <summary>Create a new booking for 1-5 seats on a trip.</summary>
    /// <remarks>
    /// Auth: PASSENGER (RS256 user token via JWKS).
    /// Idempotency-Key header required — same key + same body returns the same 201 response.
    /// Same key + different body → 422 IDEMPOTENCY_KEY_MISMATCH.
    /// Max 5 seats → 422 BOOKING_MAX_SEATS_EXCEEDED.
    /// Seat unavailable → 409 BOOKING_SEAT_UNAVAILABLE.
    /// Trip not bookable → 409 BOOKING_TRIP_NOT_BOOKABLE.
    /// Trip not found → 404 TRIP_NOT_FOUND.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = PassengerRole)]
    [ProducesResponseType(typeof(ApiResponse<CreateBookingResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateBooking(
        [FromBody] CreateBookingRequest request,
        CancellationToken ct)
    {
        var passengerUserId = GetPassengerUserId();

        var command = new CreateBookingCommand(
            PassengerUserId: passengerUserId,
            TripId: request.TripId,
            PickupStationId: request.Pickup?.StationId,
            PickupStopId: request.Pickup?.StopId,
            DropoffStationId: request.Dropoff?.StationId,
            DropoffStopId: request.Dropoff?.StopId,
            Seats: request.Seats
                .Select(s => new SeatRequest(s.SeatNumber.Trim()))
                .ToList(),
            VoucherCode: request.VoucherCode,
            PaymentMethod: request.PaymentMethod,
            ShuttlePickup: request.ShuttlePickup is null
                ? null
                : new ShuttlePickupCommand(
                    request.ShuttlePickup.Address,
                    request.ShuttlePickup.Latitude,
                    request.ShuttlePickup.Longitude));

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Create a round-trip booking as two independent linked booking rows.</summary>
    /// <remarks>
    /// Auth: PASSENGER (RS256 user token via JWKS).
    /// Idempotency-Key header required.
    /// WALLET uses one all-or-nothing batch charge for both BOOKING references.
    /// VNPay may use one BOOKING_GROUP redirect.
    /// </remarks>
    [HttpPost("round-trip")]
    [Authorize(Roles = PassengerRole)]
    [ProducesResponseType(typeof(ApiResponse<CreateRoundTripBookingResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateRoundTripBooking(
        [FromBody] CreateRoundTripBookingRequest request,
        CancellationToken ct)
    {
        var passengerUserId = GetPassengerUserId();
        var idempotencyKey = GetRequiredIdempotencyKey();

        var command = new CreateRoundTripBookingCommand(
            passengerUserId,
            idempotencyKey,
            ToCommandLeg(request.Outbound),
            ToCommandLeg(request.Return),
            request.VoucherCode,
            request.PaymentMethod);

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Edit pickup before cutoff with price-neutral-only policy.</summary>
    /// <remarks>
    /// Auth: PASSENGER (booking owner only).
    /// Idempotency-Key header required.
    /// Any fare difference is rejected with BOOKING_EDIT_PICKUP_PRICE_CHANGED.
    /// </remarks>
    [HttpPost("{bookingId:guid}/edit-pickup")]
    [Authorize(Roles = PassengerRole)]
    [ProducesResponseType(typeof(ApiResponse<EditPickupResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> EditPickup(
        [FromRoute] Guid bookingId,
        [FromBody] EditPickupRequest request,
        CancellationToken ct)
    {
        var passengerUserId = GetPassengerUserId();
        var idempotencyKey = GetRequiredIdempotencyKey();

        var command = new EditPickupCommand(
            BookingId: bookingId,
            PassengerUserId: passengerUserId,
            IdempotencyKey: idempotencyKey,
            PickupStationId: request.Pickup?.StationId,
            PickupStopId: request.Pickup?.StopId,
            PaymentMethod: request.PaymentMethod);

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status200OK, result);
    }

    /// <summary>Edit dropoff before cutoff without repricing.</summary>
    /// <remarks>
    /// Auth: PASSENGER (booking owner only).
    /// Idempotency-Key header required.
    /// Dropoff-stop edits validate route membership, allowDropoff, and stop order.
    /// </remarks>
    [HttpPost("{bookingId:guid}/edit-dropoff")]
    [Authorize(Roles = PassengerRole)]
    [ProducesResponseType(typeof(ApiResponse<EditDropoffResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> EditDropoff(
        [FromRoute] Guid bookingId,
        [FromBody] EditDropoffRequest request,
        CancellationToken ct)
    {
        var passengerUserId = GetPassengerUserId();
        var idempotencyKey = GetRequiredIdempotencyKey();

        var command = new EditDropoffCommand(
            BookingId: bookingId,
            PassengerUserId: passengerUserId,
            IdempotencyKey: idempotencyKey,
            DropoffStationId: request.Dropoff?.StationId,
            DropoffStopId: request.Dropoff?.StopId);

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status200OK, result);
    }

    /// <summary>Cancel a booking and enqueue an event-driven wallet refund.</summary>
    /// <remarks>
    /// Auth: PASSENGER (booking owner only).
    /// Idempotency-Key header required.
    /// Refund is asynchronous: response returns the preview amount; Payment credits the wallet from booking.booking.cancelled.
    /// </remarks>
    [HttpPost("{bookingId:guid}/cancel")]
    [Authorize(Roles = PassengerRole)]
    [ProducesResponseType(typeof(ApiResponse<CancelBookingResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelBooking(
        [FromRoute] Guid bookingId,
        [FromBody] CancelBookingRequest request,
        CancellationToken ct)
    {
        var passengerUserId = GetPassengerUserId();
        var idempotencyKey = GetRequiredIdempotencyKey();

        var command = new CancelBookingCommand(
            BookingId: bookingId,
            PassengerUserId: passengerUserId,
            IdempotencyKey: idempotencyKey,
            Reason: request.Reason);

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status200OK, result);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static CreateRoundTripBookingCommand.RoundTripBookingLegCommand ToCommandLeg(CreateRoundTripBookingRequest.RoundTripBookingLegRequest leg)
        => new(
            TripId: leg.TripId,
            PickupStationId: leg.Pickup?.StationId,
            PickupStopId: leg.Pickup?.StopId,
            DropoffStationId: leg.Dropoff?.StationId,
            DropoffStopId: leg.Dropoff?.StopId,
            Seats: leg.Seats
                .Select(s => new CreateRoundTripBookingCommand.RoundTripSeatRequest(s.SeatNumber.Trim()))
                .ToList(),
            ShuttlePickup: leg.ShuttlePickup is null
                ? null
                : new CreateRoundTripBookingCommand.RoundTripShuttlePickupCommand(
                    leg.ShuttlePickup.Address,
                    leg.ShuttlePickup.Latitude,
                    leg.ShuttlePickup.Longitude));

    private string GetRequiredIdempotencyKey()
    {
        var value = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            ? values.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            throw new VietRide.Shared.Application.Exceptions.CodedValidationException("VALIDATION_ERROR", "Idempotency-Key header is required.");

        return value;
    }

    private Guid GetPassengerUserId()
    {
        var sub = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");

        return userId;
    }

    private static bool IsOperator(ClaimsPrincipal user)
        => user.IsInRole("OPERATOR_ADMIN") || user.IsInRole("OPERATOR_STAFF");

    private static bool TryGetOperatorId(ClaimsPrincipal user, out Guid operatorId)
    {
        var value = user.FindFirstValue("operator_id")
            ?? user.FindFirstValue("operatorId");

        return Guid.TryParse(value, out operatorId) && operatorId != Guid.Empty;
    }
}
