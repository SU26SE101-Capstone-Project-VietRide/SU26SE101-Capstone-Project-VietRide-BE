namespace VietRide.Trip.Application.Features.Internal.Trips.Requests;

public sealed record BookSeatsRequest(
    Guid SeatLockToken,
    Guid BookingId,
    IReadOnlyList<PassengerSeatAssignmentRequest> PassengerSeatAssignments);

public sealed record BookRoundTripSeatsRequest(BookRoundTripSeatsLegRequest Outbound, BookRoundTripSeatsLegRequest Return);

public sealed record BookRoundTripSeatsLegRequest(
    Guid TripId,
    Guid SeatLockToken,
    Guid BookingId,
    IReadOnlyList<PassengerSeatAssignmentRequest> PassengerSeatAssignments);
