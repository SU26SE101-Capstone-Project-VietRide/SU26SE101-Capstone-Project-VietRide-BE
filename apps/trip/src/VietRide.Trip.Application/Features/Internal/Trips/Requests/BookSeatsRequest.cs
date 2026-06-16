namespace VietRide.Trip.Application.Features.Internal.Trips.Requests;

public sealed record BookSeatsRequest(
    Guid SeatLockToken,
    Guid BookingId,
    IReadOnlyList<PassengerSeatAssignmentRequest> PassengerSeatAssignments);
