using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public sealed record BookingPaymentTransitionSnapshot(
    Guid BookingId,
    Guid PassengerUserId,
    Guid TripId,
    Guid? SeatLockToken,
    long TotalAmount,
    Guid? VoucherUsageId,
    IReadOnlyList<PassengerSeatAssignment> PassengerSeatAssignments,
    IReadOnlyList<string> TicketCodes,
    IReadOnlyList<Guid>? TicketIds = null,
    BookingShuttleIntentSnapshot? ShuttleIntent = null,
    IReadOnlyList<BookingShuttleIntentSnapshot>? ShuttleIntents = null,
    string? BookingCode = null,
    Guid? PickupStationId = null,
    Guid? PickupStopId = null,
    Guid? DropoffStationId = null,
    Guid? DropoffStopId = null);

public sealed record BookingShuttleIntentSnapshot(
    string Address,
    decimal Latitude,
    decimal Longitude,
    string Direction = "INBOUND_TO_STATION",
    int? RoadDistanceMeters = null);
