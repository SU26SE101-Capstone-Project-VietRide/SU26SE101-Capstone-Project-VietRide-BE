using MediatR;
using VietRide.Trip.Application.Features.Internal.Trips.Requests;

namespace VietRide.Trip.Application.Features.Internal.Trips.BookRoundTripSeats;

public sealed record BookRoundTripSeatsLeg(
    Guid TripId,
    Guid SeatLockToken,
    Guid BookingId,
    IReadOnlyList<PassengerSeatAssignmentRequest> PassengerSeatAssignments);

public sealed record BookRoundTripSeatsCommand(BookRoundTripSeatsLeg Outbound, BookRoundTripSeatsLeg Return) : IRequest;
