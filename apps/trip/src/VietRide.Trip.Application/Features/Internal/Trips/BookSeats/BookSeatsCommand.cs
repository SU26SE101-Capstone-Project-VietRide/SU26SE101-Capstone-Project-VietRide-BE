using MediatR;
using VietRide.Trip.Application.Features.Internal.Trips.Requests;

namespace VietRide.Trip.Application.Features.Internal.Trips.BookSeats;

public sealed record BookSeatsCommand(
    Guid TripId,
    Guid SeatLockToken,
    Guid BookingId,
    IReadOnlyList<PassengerSeatAssignmentRequest> PassengerSeatAssignments)
    : IRequest;
