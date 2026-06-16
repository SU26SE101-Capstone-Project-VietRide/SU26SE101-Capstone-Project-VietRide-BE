using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.ReleaseSeats;

public sealed record ReleaseSeatsCommand(
    Guid TripId,
    Guid SeatLockToken,
    IReadOnlyList<string> SeatNumbers)
    : IRequest;
