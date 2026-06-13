using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Bookings.CreateBooking;
using VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;
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
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISender _sender;

    public BookingsController(ISender sender)
    {
        _sender = sender;
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
                .Select(s => new SeatRequest(
                    s.SeatNumber,
                    s.Passenger.FullName,
                    s.Passenger.PhoneNumber,
                    s.Passenger.IdNumber))
                .ToList(),
            VoucherCode: request.VoucherCode,
            PaymentMethod: request.PaymentMethod);

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
                .Select(s => new CreateRoundTripBookingCommand.RoundTripSeatRequest(
                    s.SeatNumber,
                    s.Passenger.FullName,
                    s.Passenger.PhoneNumber,
                    s.Passenger.IdNumber))
                .ToList());

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
}
