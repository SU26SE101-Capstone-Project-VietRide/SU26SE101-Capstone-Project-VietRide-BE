using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.CreateBooking;

/// <summary>
/// Command for POST /v1/bookings — creates a new booking for 1-5 seats.
/// Issued by a PASSENGER; idempotency guaranteed by Idempotency-Key header.
/// Auth claims (passengerId) are resolved in the controller and injected here.
/// </summary>
public sealed record CreateBookingCommand(
    Guid PassengerUserId,
    Guid TripId,

    // Pickup — exactly one of StationId/StopId
    Guid? PickupStationId,
    Guid? PickupStopId,

    // Dropoff — at most one of StationId/StopId
    Guid? DropoffStationId,
    Guid? DropoffStopId,

    IReadOnlyList<SeatRequest> Seats,
    string? VoucherCode,
    string PaymentMethod,
    ShuttlePickupCommand? ShuttlePickup = null) : IRequest<CreateBookingResult>;

public sealed record ShuttlePickupCommand(string Address, decimal Latitude, decimal Longitude);

/// <summary>
/// Per-seat operational booking request. Passenger PII is intentionally not collected.
/// </summary>
public sealed record SeatRequest(string SeatNumber);
